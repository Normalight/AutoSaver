# AutoSaver C# WPF 重设计

**日期**: 2026-05-06
**状态**: 已确认

---

## 概述

将 AutoSaver 从 Python/PySide6 重写为 C# WPF (.NET Framework 4.8)，实现零依赖、轻量化（~300KB exe）。保留全部核心功能，优化托盘体验。

## 技术栈

- C# / .NET Framework 4.8（Windows 10/11 自带运行时）
- WPF（主窗口 UI）
- System.Windows.Forms.NotifyIcon（托盘）
- P/Invoke（Win32 API，4 个函数）
- kernel32 INI API（GetPrivateProfileString / WritePrivateProfileString）
- 零 NuGet 依赖

## 文件结构

```
AutoSaver/
├── autosaver.exe              # 发布产物 (~300KB)
├── autosaver.ini              # 配置文件
├── generated-image-2.png      # 托盘图标
├── autosaver.log              # 滚动日志
├── AutoSaver.csproj           # 项目文件
├── App.xaml                   # WPF 入口（不创建窗口，只启动托盘）
├── App.xaml.cs
├── Models/
│   └── ProgramItem.cs         # 数据模型
├── Services/
│   ├── ConfigService.cs       # INI 读写 + 默认值
│   ├── ProcessMonitor.cs      # DispatcherTimer 轮询进程
│   ├── SaveScheduler.cs       # 每程序一个 Timer
│   └── WindowService.cs       # P/Invoke 窗口操作
└── Views/
    ├── MainWindow.xaml/.cs    # 主窗口，Show 时 new，关闭时销毁
    ├── AddEditDialog.xaml/.cs # 添加/编辑程序对话框
    └── ProcessPickerDialog.xaml/.cs  # 运行中进程选取列表
```

## INI 配置文件

`autosaver.ini` 与 exe 同级，首次运行自动生成。

```ini
[global]
start_with_windows = false
check_interval_sec = 3
minimize_to_tray_on_close = true

[programs]
count = 0

[program.1]
id = <guid>
name = SAI2
exe = sai2.exe
enabled = true
save_interval_sec = 300
```

读写用 `kernel32.dll` 的 `GetPrivateProfileString` / `WritePrivateProfileString`。

## 架构与数据流

```
ProcessMonitor (DispatcherTimer, 每 check_interval_sec 秒)
  → Process.GetProcesses() 枚举进程名
  → 对比每个 ProgramItem.exe 是否在运行
  → 状态变化时：
      → 更新内存中程序状态缓存
      → 通知 SaveScheduler.Start/Stop
      → 更新托盘 ContextMenuStrip 菜单项（仅名称包含状态圆点）

SaveScheduler (每启用程序一个 System.Timers.Timer)
  → 按 save_interval_sec 定时触发
  → 调 WindowService.EnumWindows(exe) → hwnd[]
  → 对每个 hwnd 调 WindowService.SendCtrlS(hwnd)
  → 写日志

托盘 (NotifyIcon)
  → ContextMenuStrip.Opening 事件 → 动态重建菜单：
      ● programName  (运行中)
      ○ programName  (未检测到)
      ───────────
      显示主窗口
      退出
  → 左键双击 → 显示主窗口

主窗口 (MainWindow)
  → ShowDialog() 创建显示
  → 关闭时 Close() + Dispose → GC 回收
  → 再次打开重新 new MainWindow()
  → 配置变更通过 ConfigService 持久化，Monitor/Scheduler 实时读取
```

## 模块 API

### ConfigService

```csharp
static class ConfigService
{
    static string IniPath { get; }  // exe 同级 autosaver.ini
    static GlobalConfig LoadGlobal();
    static void SaveGlobal(GlobalConfig cfg);
    static List<ProgramItem> LoadPrograms();
    static void SavePrograms(List<ProgramItem> programs);
}
```

### WindowService

```csharp
static class WindowService
{
    // P/Invoke: EnumWindows, GetWindowThreadProcessId, IsWindowVisible, PostMessage
    static List<IntPtr> GetWindowsByExe(string exeName);
    static bool SendCtrlS(IntPtr hwnd);
    static string GetWindowTitle(IntPtr hwnd);
}
```

### ProcessMonitor

```csharp
class ProcessMonitor
{
    event Action<ProgramItem, bool> StatusChanged;  // program, running
    void Start(int checkIntervalSec);
    void Stop();
    bool GetStatus(string programId);  // 当前运行状态
    void RefreshPrograms(List<ProgramItem> programs);
}
```

### SaveScheduler

```csharp
class SaveScheduler
{
    event Action<string, string, int> SaveDone;  // programId, timestamp, windowCount
    void AddProgram(ProgramItem prog);
    void RemoveProgram(string programId);
    void UpdateProgram(ProgramItem prog);
    void SetRunning(string programId, bool running);
    void StopAll();
}
```

## UI 设计

### 主窗口

固定宽度 520px，最小高度 300px。表头：程序名 | 状态 | 间隔 | 操作。
底部：开机自启复选框 + 上次保存状态栏。

关闭行为：`minimize_to_tray_on_close=true` 时隐藏到托盘（销毁窗口），否则退出应用。

### 添加/编辑对话框

```
┌─ 添加程序 ──────────────────────────────┐
│  [📁 选择本地程序]  [📋 正在运行中选取]   │
│                                          │
│  显示名称: [___________________________] │
│  进程名:   [___________________________] │
│  保存间隔: [5] 分钟                      │
│  ☑ 启用                                 │
│                [取消]  [确定]             │
└──────────────────────────────────────────┘
```

两种添加方式，均为图标按钮：

| 按钮 | 行为 |
|---|---|
| 📁 选择本地程序 | 打开 OpenFileDialog，过滤 `*.exe`。初始目录 `C:\Program Files`（不存在则回退桌面）。选中后直接覆盖进程名和显示名称（不留原名） |
| 📋 正在运行中选取 | 弹出 ProcessPickerDialog（简单列表窗口），列出当前所有非系统进程。用户双击选中后覆盖进程名和显示名称 |

规则：
- 显示名称不做首字母大写，保持原始进程名
- 文件对话框过滤仅 `*.exe`；进程选取过滤系统进程（svchost.exe, csrss.exe, system, smss.exe, wininit.exe, services.exe, lsass.exe, winlogon.exe, explorer.exe, taskmgr.exe, autosaver.exe）
- 两个按钮覆盖已有内容不提示（按钮意图明确）
- 编辑模式同样可用两个按钮（允许更换目标进程）

### ProcessPickerDialog

简单的模态窗口，显示一个 ListView 列出当前所有非系统进程名（去重、字母排序）。双击或选中后点确定直接关闭窗口返回进程名。

### 托盘菜单

```
● SAI2                    ← 程序状态行（不可点击）
○ Photoshop
──────────────────────
显示主窗口                 ← 功能性菜单项
退出
```

- 圆点颜色：● 绿色 = 运行中，○ 灰色 = 未检测到
- 在 ContextMenuStrip.Opening 事件中每次重建
- 程序名带圆点的那几行：通过 `OwnerDraw` 或设置 `Enabled = false` 禁用点击

## 边界情况

| 情况 | 处理 |
|------|------|
| INI 文件损坏 | 记录日志，用默认值启动，不覆盖原文件 |
| 首次运行无 INI | 自动创建默认 INI |
| 程序多窗口 | 对全部顶层可见窗口发送 Ctrl+S |
| 程序被删除 | 立即停止对应 Timer |
| 开机自启写注册表失败 | 弹出提示，复选框恢复原状 |
| 窗口关闭 | minimize_to_tray_on_close 为 true 时销毁窗口对象，false 时退出应用 |

## 打包

WPF 项目 Release 构建，输出到 `bin/Release/`，复制 `autosaver.exe` + `generated-image-2.png` 即可分发。
