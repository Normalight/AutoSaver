# AutoSaver 设计文档

**日期**: 2026-05-06  
**状态**: 已确认

---

## 概述

AutoSaver 是一个 Windows 桌面工具，解决 SAI2、Photoshop 等绘图软件没有自动保存功能的问题。它在后台监控指定进程，检测到目标程序运行时自动定时向其所有窗口发送 `Ctrl+S`。

---

## 技术栈

| 层级 | 技术 |
|------|------|
| 语言 | Python 3.11+ |
| GUI | PySide6 |
| Windows API | pywin32 (win32gui, win32api, win32process, win32con) |
| 进程枚举 | psutil |
| 打包 | PyInstaller (`--onedir`) |

无额外第三方依赖，仅以上标准库。

---

## 文件结构

```
AutoSaver/
├── autosaver.exe          # 打包产物
├── config.json            # 用户配置（首次运行自动生成）
├── autosaver.log          # 滚动日志，最大 1MB，保留 1 个备份
├── requirements.txt
├── build.bat              # 一键打包脚本
├── docs/
│   └── superpowers/specs/
│       └── 2026-05-06-autosaver-design.md
└── src/
    ├── main.py            # 入口，启动 QApplication + 托盘
    ├── core/
    │   ├── config.py      # 读写 config.json
    │   ├── monitor.py     # 进程监控线程
    │   ├── scheduler.py   # 每个程序的定时保存调度器
    │   └── saver.py       # 枚举窗口 + 发送 Ctrl+S
    └── ui/
        ├── main_window.py # 主窗口（程序列表）
        ├── add_dialog.py  # 添加/编辑程序对话框
        └── tray.py        # 系统托盘图标与菜单
```

---

## 配置文件格式

`config.json` 与 exe 同级，首次运行若不存在则自动创建默认值。

```json
{
  "global": {
    "start_with_windows": false,
    "check_interval_sec": 3,
    "minimize_to_tray_on_close": true
  },
  "programs": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "name": "SAI2",
      "exe": "sai2.exe",
      "enabled": true,
      "save_interval_sec": 300
    }
  ]
}
```

字段说明：
- `check_interval_sec`：进程监控轮询间隔，默认 3 秒
- `save_interval_sec`：每个程序独立的保存间隔，单位秒
- `minimize_to_tray_on_close`：点关闭按钮时最小化到托盘而非退出

---

## 架构与数据流

```
ProcessMonitor (后台线程)
  每 check_interval_sec 秒枚举系统进程
  → 若目标 exe 从"未运行"变为"运行中"：通知 Scheduler 启动
  → 若目标 exe 从"运行中"变为"未运行"：通知 Scheduler 停止

SaveScheduler (每个启用程序一个线程)
  按 save_interval_sec 定时触发
  → 调用 saver.get_windows_by_exe(exe) 枚举所有顶层可见窗口
  → 对每个 hwnd 调用 saver.send_ctrl_s(hwnd)
  → 记录日志，通知 UI 更新"上次保存时间"

UI (PySide6 主线程)
  通过 Qt 信号接收 Monitor/Scheduler 的状态变更
  → 更新列表中的状态列（运行中 / 未检测到）
  → 更新底部状态栏"上次保存"信息
```

线程间通信使用 Qt 信号槽（`pyqtSignal` / `Signal`），不使用共享变量。

---

## 模块设计

### core/config.py

- `load_config(path) -> dict`：读取并返回配置，文件不存在时返回默认值
- `save_config(path, data)`：原子写入（先写临时文件再 rename）
- `get_config_path() -> Path`：返回 exe 同级目录下的 config.json 路径

### core/saver.py

- `get_windows_by_exe(exe_name) -> list[int]`：枚举所有顶层可见窗口，返回属于目标进程的 hwnd 列表
- `send_ctrl_s(hwnd)`：用 `PostMessage` 后台发送 Ctrl+S，不抢焦点

```python
# 发送逻辑（不抢焦点）
PostMessage(hwnd, WM_KEYDOWN, VK_CONTROL, 0)
PostMessage(hwnd, WM_KEYDOWN, ord('S'), 0)
PostMessage(hwnd, WM_KEYUP,   ord('S'), 0)
PostMessage(hwnd, WM_KEYUP,   VK_CONTROL, 0)
```

### core/monitor.py

- `ProcessMonitor(QThread)`：持续轮询，维护每个程序的运行状态
- 信号：`status_changed(program_id: str, running: bool)`

### core/scheduler.py

- `SaveScheduler(QThread)`：接收 Monitor 信号，管理每个程序的定时器
- 信号：`save_done(program_id: str, timestamp: str, window_count: int)`
- 全部暂停/恢复通过 `pause()` / `resume()` 方法控制

---

## UI 设计

### 主窗口

固定宽度 520px，最小高度 300px。

```
┌─ AutoSaver ─────────────────────────────────────┐
│                                                   │
│  程序名        状态           间隔      操作      │
│  ─────────────────────────────────────────────── │
│  SAI2         ● 运行中       5 分钟   [编辑][删] │
│  Photoshop    ○ 未检测到     3 分钟   [编辑][删] │
│                                                   │
│  [+ 添加程序]                    [全部暂停/恢复]  │
│  ─────────────────────────────────────────────── │
│  ☑ 开机自启    上次保存: 14:32:05 (SAI2, 3个窗口)│
└───────────────────────────────────────────────────┘
```

点击关闭按钮：若 `minimize_to_tray_on_close=true` 则最小化到托盘，否则退出。

### 添加/编辑对话框

```
┌─ 添加程序 ──────────────────┐
│  显示名称: [SAI2           ] │
│  进程名:   [sai2.exe       ] [从运行中选择 ▼] │
│  保存间隔: [5] 分钟          │
│  ☑ 启用                      │
│              [取消]  [确定]  │
└──────────────────────────────┘
```

"从运行中选择"下拉列表实时枚举当前运行的进程 exe 名，方便用户填写正确的进程名。

### 系统托盘

右键菜单：
- 显示主窗口
- 暂停所有 / 恢复所有（切换）
- 退出

托盘图标：正常状态为默认图标；全部暂停时图标变灰。

---

## 开机自启

写注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，键名 `AutoSaver`，值为 exe 绝对路径。UI 复选框直接读写注册表，无需重启生效。

---

## 日志

使用 Python 标准库 `logging`，`RotatingFileHandler`，最大 1MB，保留 1 个备份文件。日志文件 `autosaver.log` 与 exe 同级。

日志内容：进程状态变更、每次保存操作（程序名、窗口数、时间戳）、错误信息。

---

## 打包

```bat
:: build.bat
pyinstaller --onedir --windowed --name autosaver --icon assets/icon.ico src/main.py
```

产物在 `dist/autosaver/` 目录，用户将整个文件夹复制到任意位置即可使用。

---

## 边界情况处理

| 情况 | 处理方式 |
|------|----------|
| 文件未保存过，Ctrl+S 弹出另存为对话框 | 不干预，让用户手动处理 |
| 目标程序有多个窗口 | 对所有顶层可见窗口逐一发送 |
| 程序运行中被删除出列表 | 立即停止对应 Scheduler |
| config.json 损坏无法解析 | 记录错误日志，使用默认配置启动，不覆盖原文件 |
| 注册表写入失败（权限不足） | 弹出提示，复选框恢复原状 |
