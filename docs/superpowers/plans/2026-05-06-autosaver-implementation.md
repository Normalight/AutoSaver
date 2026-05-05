# AutoSaver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build AutoSaver — a Windows system tray app that monitors drawing applications and auto-sends Ctrl+S at configurable intervals.

**Architecture:** 7 Python modules across 3 layers. Qt signals for thread communication (no shared mutable state). Foundation layer (config, saver) has no deps; core layer (monitor, scheduler) depends on foundation; UI layer (tray, add_dialog, main_window, main) depends on core.

**Tech Stack:** Python 3.11+, PySide6, pywin32, psutil, PyInstaller

---

## File Structure

```
src/
├── main.py              # Entry: QApplication + tray + main window wiring
├── core/
│   ├── config.py        # Read/write config.json (atomic write)
│   ├── monitor.py        # ProcessMonitor(QThread): poll for target exe
│   ├── scheduler.py      # SaveScheduler(QThread): per-program timers
│   └── saver.py          # Window enumeration + PostMessage Ctrl+S
└── ui/
    ├── main_window.py    # Program list with status/interval/actions
    ├── add_dialog.py     # Add/edit dialog with process picker
    └── tray.py           # System tray icon + context menu
```

## Dependency Graph

```
Group A (parallel, no deps):
  Task 1: core/config.py
  Task 2: core/saver.py

Group B (parallel, depends on A):
  Task 3: core/monitor.py
  Task 4: core/scheduler.py

Group C (parallel, depends on B):
  Task 5: ui/tray.py
  Task 6: ui/add_dialog.py
  Task 7: ui/main_window.py

Group D (depends on C):
  Task 8: main.py (wiring)
  Task 9: requirements.txt + build.bat
```

---

### Task 1: core/config.py

**Files:**
- Create: `src/core/config.py`
- Create: `src/core/__init__.py`

- [ ] **Step 1: Write config.py**

```python
"""Configuration management for AutoSaver.

Reads/writes config.json next to the executable. First run auto-creates defaults.
Atomic writes via temp file + rename to prevent corruption.
"""

import json
import logging
import os
import sys
import uuid
from pathlib import Path
from tempfile import NamedTemporaryFile

logger = logging.getLogger(__name__)

DEFAULT_CONFIG = {
    "global": {
        "start_with_windows": False,
        "check_interval_sec": 3,
        "minimize_to_tray_on_close": True,
    },
    "programs": [],
}


def get_config_path() -> Path:
    if getattr(sys, "frozen", False):
        base = Path(sys.executable).parent
    else:
        base = Path(__file__).resolve().parent.parent.parent
    return base / "config.json"


def _deep_merge(default: dict, override: dict) -> dict:
    result = default.copy()
    for key, value in override.items():
        if key in result and isinstance(result[key], dict) and isinstance(value, dict):
            result[key] = _deep_merge(result[key], value)
        else:
            result[key] = value
    return result


def load_config(path: Path | None = None) -> dict:
    if path is None:
        path = get_config_path()

    if not path.exists():
        logger.info("config.json not found, creating with defaults")
        save_config(path, DEFAULT_CONFIG)
        return DEFAULT_CONFIG

    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        merged = _deep_merge(DEFAULT_CONFIG, data)
        for prog in merged.get("programs", []):
            if "id" not in prog:
                prog["id"] = str(uuid.uuid4())
        return merged
    except (json.JSONDecodeError, OSError) as e:
        logger.error("Failed to parse config.json: %s, using defaults", e)
        return DEFAULT_CONFIG


def save_config(data: dict, path: Path | None = None) -> None:
    if path is None:
        path = get_config_path()

    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = NamedTemporaryFile(
        mode="w", encoding="utf-8", delete=False, dir=path.parent, suffix=".tmp"
    )
    try:
        json.dump(data, tmp, indent=2, ensure_ascii=False)
        tmp.flush()
        os.fsync(tmp.fileno())
        tmp.close()
        os.replace(tmp.name, str(path))
    except Exception:
        tmp.close()
        try:
            os.unlink(tmp.name)
        except OSError:
            pass
        raise
```

- [ ] **Step 2: Create `src/core/__init__.py` (empty)**

- [ ] **Step 3: Commit**

```bash
git add src/core/__init__.py src/core/config.py
git commit -m "feat: add config module with atomic JSON read/write"
```

---

### Task 2: core/saver.py

**Files:**
- Create: `src/core/saver.py`

- [ ] **Step 1: Write saver.py**

```python
"""Window enumeration and keystroke injection for AutoSaver.

Uses pywin32 to find visible top-level windows belonging to a target process
and send Ctrl+S via PostMessage (no focus stealing).
"""

import logging
import ctypes
from ctypes import wintypes

import psutil
import win32con
import win32gui
import win32process

logger = logging.getLogger(__name__)

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32


def get_windows_by_exe(exe_name: str) -> list[int]:
    """Return HWNDs of all visible top-level windows for the given exe name."""
    exe_lower = exe_name.lower()
    pids = set()
    for proc in psutil.process_iter(["pid", "name"]):
        try:
            if proc.info["name"] and proc.info["name"].lower() == exe_lower:
                pids.add(proc.info["pid"])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue

    if not pids:
        return []

    hwnds = []

    def callback(hwnd: int, _):
        if not win32gui.IsWindowVisible(hwnd):
            return True
        if not win32gui.IsWindowEnabled(hwnd):
            return True
        _, found_pid = win32process.GetWindowThreadProcessId(hwnd)
        if found_pid in pids:
            hwnds.append(hwnd)
        return True

    win32gui.EnumWindows(callback, None)
    return hwnds


def send_ctrl_s(hwnd: int) -> bool:
    """Send Ctrl+S to the target window via PostMessage. Returns True on success."""
    try:
        win32gui.PostMessage(hwnd, win32con.WM_KEYDOWN, win32con.VK_CONTROL, 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYDOWN, ord("S"), 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYUP, ord("S"), 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYUP, win32con.VK_CONTROL, 0)
        return True
    except Exception:
        logger.exception("Failed to send Ctrl+S to hwnd %d", hwnd)
        return False


def get_window_title(hwnd: int) -> str:
    """Get window title for logging/display purposes."""
    try:
        return win32gui.GetWindowText(hwnd)
    except Exception:
        return ""
```

- [ ] **Step 2: Commit**

```bash
git add src/core/saver.py
git commit -m "feat: add saver module for window enumeration and Ctrl+S injection"
```

---

### Task 3: core/monitor.py

**Files:**
- Create: `src/core/monitor.py`

- [ ] **Step 1: Write monitor.py**

```python
"""Process monitor thread for AutoSaver.

Polls running processes at configurable intervals and emits Qt signals
when a target program's running status changes.
"""

import logging
import time

import psutil
from PySide6.QtCore import QThread, Signal

logger = logging.getLogger(__name__)


class ProcessMonitor(QThread):
    status_changed = Signal(str, bool)  # program_id, running

    def __init__(self, parent=None):
        super().__init__(parent)
        self._programs: list[dict] = []
        self._interval = 3
        self._running = False
        self._prev_state: dict[str, bool] = {}

    def configure(self, programs: list[dict], interval_sec: int) -> None:
        self._programs = programs
        self._interval = interval_sec

    def stop_monitor(self) -> None:
        self._running = False

    def run(self) -> None:
        self._running = True
        while self._running:
            self._poll()
            time.sleep(self._interval)

    def _poll(self) -> None:
        try:
            running_exes: set[str] = set()
            for proc in psutil.process_iter(["name"]):
                try:
                    name = proc.info["name"]
                    if name:
                        running_exes.add(name.lower())
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    continue
        except Exception:
            logger.exception("Process enumeration failed")
            return

        for prog in self._programs:
            if not prog.get("enabled", True):
                continue
            pid = prog["id"]
            exe = prog.get("exe", "").lower()
            is_running = exe in running_exes
            prev = self._prev_state.get(pid)
            if prev != is_running:
                self._prev_state[pid] = is_running
                logger.info(
                    "Program %s (%s) status: %s",
                    prog.get("name", pid),
                    prog.get("exe", ""),
                    "running" if is_running else "stopped",
                )
                self.status_changed.emit(pid, is_running)
```

- [ ] **Step 2: Commit**

```bash
git add src/core/monitor.py
git commit -m "feat: add process monitor thread with Qt signal emission"
```

---

### Task 4: core/scheduler.py

**Files:**
- Create: `src/core/scheduler.py`

- [ ] **Step 1: Write scheduler.py**

```python
"""Save scheduler for AutoSaver.

Manages per-program save timers. Each enabled program gets a timer
that fires at its configured interval, calling the saver to send Ctrl+S.
Signals back to UI the last save timestamp and window count.
"""

import logging
import time
from datetime import datetime

from PySide6.QtCore import QThread, Signal

import src.core.saver as saver_

logger = logging.getLogger(__name__)


class SaveScheduler(QThread):
    save_done = Signal(str, str, int)  # program_id, timestamp, window_count

    def __init__(self, parent=None):
        super().__init__(parent)
        self._programs: dict[str, dict] = {}
        self._timers: dict[str, float] = {}
        self._running_programs: set[str] = set()
        self._active = True
        self._shutdown = False

    def add_program(self, program: dict) -> None:
        pid = program["id"]
        self._programs[pid] = program
        self._timers[pid] = time.time()

    def remove_program(self, program_id: str) -> None:
        self._programs.pop(program_id, None)
        self._timers.pop(program_id, None)
        self._running_programs.discard(program_id)

    def update_program(self, program: dict) -> None:
        pid = program["id"]
        self._programs[pid] = program
        if pid not in self._timers:
            self._timers[pid] = time.time()

    def set_running(self, program_id: str, running: bool) -> None:
        if running:
            self._running_programs.add(program_id)
            self._timers[program_id] = time.time()
        else:
            self._running_programs.discard(program_id)

    def pause_all(self) -> None:
        self._active = False
        logger.info("All save schedulers paused")

    def resume_all(self) -> None:
        self._active = True
        for pid in self._running_programs:
            self._timers[pid] = time.time()
        logger.info("All save schedulers resumed")

    @property
    def is_paused(self) -> bool:
        return not self._active

    def stop_scheduler(self) -> None:
        self._shutdown = True

    def run(self) -> None:
        while not self._shutdown:
            if self._active:
                now = time.time()
                for pid, prog in list(self._programs.items()):
                    if pid not in self._running_programs:
                        continue
                    interval = prog.get("save_interval_sec", 300)
                    if now - self._timers.get(pid, now) >= interval:
                        self._timers[pid] = now
                        self._do_save(prog)
            time.sleep(1)

    def _do_save(self, prog: dict) -> None:
        exe = prog.get("exe", "")
        try:
            hwnds = saver_.get_windows_by_exe(exe)
            for hwnd in hwnds:
                saver_.send_ctrl_s(hwnd)
            timestamp = datetime.now().strftime("%H:%M:%S")
            logger.info(
                "Saved %s (%s): %d window(s) at %s",
                prog.get("name", prog["id"]),
                exe,
                len(hwnds),
                timestamp,
            )
            self.save_done.emit(prog["id"], timestamp, len(hwnds))
        except Exception:
            logger.exception("Save failed for %s", prog.get("name", prog["id"]))
```

- [ ] **Step 2: Commit**

```bash
git add src/core/scheduler.py
git commit -m "feat: add save scheduler with per-program timers"
```

---

### Task 5: ui/tray.py

**Files:**
- Create: `src/ui/__init__.py`
- Create: `src/ui/tray.py`

- [ ] **Step 1: Write tray.py**

```python
"""System tray icon and context menu for AutoSaver."""

import logging
from pathlib import Path

from PySide6.QtGui import QAction, QIcon
from PySide6.QtWidgets import QApplication, QMenu, QSystemTrayIcon

logger = logging.getLogger(__name__)


def _get_icon_path() -> str:
    base = Path(__file__).resolve().parent.parent.parent
    return str(base / "assets" / "icon.ico")


class SystemTray(QSystemTrayIcon):
    def __init__(self, parent=None):
        icon_path = _get_icon_path()
        icon = QIcon(icon_path) if Path(icon_path).exists() else QIcon()
        super().__init__(icon, parent)
        self.setToolTip("AutoSaver")

        menu = QMenu()

        self._show_action = QAction("显示主窗口")
        self._show_action.triggered.connect(self._on_show)
        menu.addAction(self._show_action)

        self._pause_action = QAction("暂停所有")
        self._pause_action.triggered.connect(self._on_toggle_pause)
        menu.addAction(self._pause_action)

        menu.addSeparator()

        quit_action = QAction("退出")
        quit_action.triggered.connect(self._on_quit)
        menu.addAction(quit_action)

        self.setContextMenu(menu)
        self.activated.connect(self._on_activated)

        self._paused = False
        self.show_requested = None
        self.pause_toggled = None
        self.quit_requested = None

    def _on_show(self) -> None:
        if self.show_requested:
            self.show_requested()

    def _on_toggle_pause(self) -> None:
        if self.pause_toggled:
            self.pause_toggled()

    def _on_quit(self) -> None:
        if self.quit_requested:
            self.quit_requested()

    def _on_activated(self, reason: QSystemTrayIcon.ActivationReason) -> None:
        if reason == QSystemTrayIcon.DoubleClick:
            self._on_show()

    def set_paused_state(self, paused: bool) -> None:
        self._paused = paused
        self._pause_action.setText("恢复所有" if paused else "暂停所有")
```

- [ ] **Step 2: Commit**

```bash
git add src/ui/__init__.py src/ui/tray.py
git commit -m "feat: add system tray with context menu"
```

---

### Task 6: ui/add_dialog.py

**Files:**
- Create: `src/ui/add_dialog.py`

- [ ] **Step 1: Write add_dialog.py**

```python
"""Add/Edit program dialog for AutoSaver."""

import uuid

import psutil
from PySide6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QLineEdit,
    QPushButton,
    QSpinBox,
    QVBoxLayout,
)

EXE_BLACKLIST = {"system idle process", "system", "svchost.exe", "csrss.exe",
                 "smss.exe", "wininit.exe", "services.exe", "lsass.exe",
                 "winlogon.exe", "explorer.exe", "taskmgr.exe", "autosaver.exe"}


def _get_running_exes() -> list[str]:
    exes: set[str] = set()
    for proc in psutil.process_iter(["name"]):
        try:
            name = proc.info["name"]
            if name and name.lower() not in EXE_BLACKLIST:
                exes.add(name)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    return sorted(exes, key=str.lower)


class AddEditDialog(QDialog):
    def __init__(self, parent=None, program: dict | None = None):
        super().__init__(parent)
        self._program = program
        self._editing = program is not None
        self.setWindowTitle("编辑程序" if self._editing else "添加程序")
        self.setMinimumWidth(380)
        self._setup_ui()
        if program:
            self._load_program(program)

    def _setup_ui(self) -> None:
        layout = QVBoxLayout(self)
        form = QFormLayout()

        self._name_edit = QLineEdit()
        self._name_edit.setPlaceholderText("例如: SAI2")
        form.addRow("显示名称:", self._name_edit)

        exe_layout = QVBoxLayout()
        self._exe_edit = QLineEdit()
        self._exe_edit.setPlaceholderText("例如: sai2.exe")
        exe_layout.addWidget(self._exe_edit)
        self._process_picker = QComboBox()
        self._process_picker.addItem("-- 从运行中选择 --")
        self._process_picker.addItems(_get_running_exes())
        self._process_picker.currentTextChanged.connect(self._on_process_picked)
        exe_layout.addWidget(self._process_picker)
        form.addRow("进程名:", exe_layout)

        self._interval_spin = QSpinBox()
        self._interval_spin.setRange(1, 3600)
        self._interval_spin.setValue(5)
        self._interval_spin.setSuffix(" 分钟")
        form.addRow("保存间隔:", self._interval_spin)

        self._enabled_check = QCheckBox("启用")
        self._enabled_check.setChecked(True)
        form.addRow("", self._enabled_check)

        layout.addLayout(form)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _load_program(self, prog: dict) -> None:
        self._name_edit.setText(prog.get("name", ""))
        self._exe_edit.setText(prog.get("exe", ""))
        self._interval_spin.setValue(prog.get("save_interval_sec", 300) // 60)
        self._enabled_check.setChecked(prog.get("enabled", True))

    def _on_process_picked(self, text: str) -> None:
        if text and text != "-- 从运行中选择 --":
            self._exe_edit.setText(text)

    def get_program(self) -> dict:
        if self._program:
            prog = self._program.copy()
        else:
            prog = {"id": str(uuid.uuid4())}
        prog["name"] = self._name_edit.text().strip()
        prog["exe"] = self._exe_edit.text().strip()
        prog["save_interval_sec"] = self._interval_spin.value() * 60
        prog["enabled"] = self._enabled_check.isChecked()
        return prog
```

- [ ] **Step 2: Commit**

```bash
git add src/ui/add_dialog.py
git commit -m "feat: add program add/edit dialog with process picker"
```

---

### Task 7: ui/main_window.py

**Files:**
- Create: `src/ui/main_window.py`

- [ ] **Step 1: Write main_window.py**

```python
"""Main window for AutoSaver — program list, status, and controls."""

import logging
from datetime import datetime

import winreg
from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QCheckBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QMainWindow,
    QPushButton,
    QStatusBar,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from src.ui.add_dialog import AddEditDialog

logger = logging.getLogger(__name__)

REG_PATH = r"Software\Microsoft\Windows\CurrentVersion\Run"
REG_KEY = "AutoSaver"


class MainWindow(QMainWindow):
    def __init__(self, config: dict, parent=None):
        super().__init__(parent)
        self._config = config
        self._paused = False
        self._last_save_info: dict[str, tuple[str, int]] = {}  # prog_name -> (timestamp, count)
        self.setWindowTitle("AutoSaver")
        self.setFixedWidth(520)
        self.setMinimumHeight(300)
        self._setup_ui()
        self._refresh_table()

    def _setup_ui(self) -> None:
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)

        self._table = QTableWidget(0, 4)
        self._table.setHorizontalHeaderLabels(["程序名", "状态", "间隔", "操作"])
        self._table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self._table.horizontalHeader().setSectionResizeMode(1, QHeaderView.Fixed)
        self._table.horizontalHeader().setSectionResizeMode(2, QHeaderView.Fixed)
        self._table.horizontalHeader().setSectionResizeMode(3, QHeaderView.Fixed)
        self._table.setColumnWidth(1, 100)
        self._table.setColumnWidth(2, 80)
        self._table.setColumnWidth(3, 120)
        self._table.setSelectionBehavior(QTableWidget.SelectRows)
        self._table.verticalHeader().setVisible(False)
        layout.addWidget(self._table)

        btn_layout = QHBoxLayout()
        self._add_btn = QPushButton("+ 添加程序")
        self._add_btn.clicked.connect(self._on_add)
        btn_layout.addWidget(self._add_btn)
        btn_layout.addStretch()
        self._pause_btn = QPushButton("全部暂停")
        self._pause_btn.clicked.connect(self._on_toggle_pause)
        btn_layout.addWidget(self._pause_btn)
        layout.addLayout(btn_layout)

        status = QStatusBar()
        self._startup_check = QCheckBox("开机自启")
        self._startup_check.setChecked(self._get_startup_registry())
        self._startup_check.stateChanged.connect(self._on_startup_changed)
        status.addPermanentWidget(self._startup_check)
        self._status_label = QLabel("")
        status.addWidget(self._status_label, 1)
        self.setStatusBar(status)

    def _refresh_table(self) -> None:
        programs = self._config.get("programs", [])
        self._table.setRowCount(len(programs))
        for row, prog in enumerate(programs):
            name_item = QTableWidgetItem(prog.get("name", ""))
            name_item.setData(Qt.UserRole, prog["id"])
            self._table.setItem(row, 0, name_item)

            status_text = self._get_status_text(prog["id"])
            self._table.setItem(row, 1, QTableWidgetItem(status_text))

            interval_min = prog.get("save_interval_sec", 300) // 60
            self._table.setItem(row, 2, QTableWidgetItem(f"{interval_min} 分钟"))

            action_widget = QWidget()
            action_layout = QHBoxLayout(action_widget)
            action_layout.setContentsMargins(0, 0, 0, 0)
            edit_btn = QPushButton("编辑")
            edit_btn.clicked.connect(lambda checked, p=prog: self._on_edit(p))
            del_btn = QPushButton("删除")
            del_btn.clicked.connect(lambda checked, pid=prog["id"]: self._on_delete(pid))
            action_layout.addWidget(edit_btn)
            action_layout.addWidget(del_btn)
            self._table.setCellWidget(row, 3, action_widget)

        self._update_status_bar()

    def _get_status_text(self, program_id: str) -> str:
        return "● 运行中"  # placeholder, updated by signals

    def _on_add(self) -> None:
        dlg = AddEditDialog(self)
        if dlg.exec():
            prog = dlg.get_program()
            self._config.setdefault("programs", []).append(prog)
            self._refresh_table()
            if hasattr(self, "program_added"):
                self.program_added(prog)

    def _on_edit(self, prog: dict) -> None:
        dlg = AddEditDialog(self, prog)
        if dlg.exec():
            updated = dlg.get_program()
            programs = self._config["programs"]
            for i, p in enumerate(programs):
                if p["id"] == updated["id"]:
                    programs[i] = updated
                    break
            self._refresh_table()
            if hasattr(self, "program_edited"):
                self.program_edited(updated)

    def _on_delete(self, program_id: str) -> None:
        self._config["programs"] = [
            p for p in self._config.get("programs", []) if p["id"] != program_id
        ]
        self._refresh_table()
        if hasattr(self, "program_deleted"):
            self.program_deleted(program_id)

    def _on_toggle_pause(self) -> None:
        self._paused = not self._paused
        self._pause_btn.setText("全部恢复" if self._paused else "全部暂停")
        if hasattr(self, "pause_toggled"):
            self.pause_toggled(self._paused)

    def _on_startup_changed(self, state: int) -> None:
        enabled = state == Qt.Checked.value
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, REG_PATH, 0, winreg.KEY_SET_VALUE
            )
            if enabled:
                import sys
                winreg.SetValueEx(key, REG_KEY, 0, winreg.REG_SZ, sys.executable)
            else:
                try:
                    winreg.DeleteValue(key, REG_KEY)
                except FileNotFoundError:
                    pass
            winreg.CloseKey(key)
        except OSError:
            logger.exception("Failed to update startup registry")
            self._startup_check.setChecked(not enabled)

    def _get_startup_registry(self) -> bool:
        try:
            key = winreg.OpenKey(
                winreg.HKEY_CURRENT_USER, REG_PATH, 0, winreg.KEY_READ
            )
            try:
                winreg.QueryValueEx(key, REG_KEY)
                winreg.CloseKey(key)
                return True
            except FileNotFoundError:
                winreg.CloseKey(key)
                return False
        except OSError:
            return False

    def update_program_status(self, program_id: str, running: bool) -> None:
        for row in range(self._table.rowCount()):
            item = self._table.item(row, 0)
            if item and item.data(Qt.UserRole) == program_id:
                self._table.item(row, 1).setText(
                    "● 运行中" if running else "○ 未检测到"
                )
                break

    def update_last_save(self, program_id: str, timestamp: str, window_count: int) -> None:
        for prog in self._config.get("programs", []):
            if prog["id"] == program_id:
                self._last_save_info[prog["name"]] = (timestamp, window_count)
                break
        self._update_status_bar()

    def _update_status_bar(self) -> None:
        if self._last_save_info:
            parts = []
            for name, (ts, count) in self._last_save_info.items():
                parts.append(f"{name} {ts} ({count}窗口)")
            self._status_label.setText("上次保存: " + ", ".join(parts))
        else:
            self._status_label.setText("")

    def get_config(self) -> dict:
        return self._config

    def set_paused_state(self, paused: bool) -> None:
        self._paused = paused
        self._pause_btn.setText("全部恢复" if paused else "全部暂停")

    def closeEvent(self, event) -> None:
        if self._config.get("global", {}).get("minimize_to_tray_on_close", True):
            event.ignore()
            self.hide()
        else:
            event.accept()
```

- [ ] **Step 2: Commit**

```bash
git add src/ui/main_window.py
git commit -m "feat: add main window with program list and controls"
```

---

### Task 8: main.py (Entry Point + Wiring)

**Files:**
- Create: `src/main.py`

- [ ] **Step 1: Write main.py**

```python
"""AutoSaver entry point.

Sets up logging, QApplication, system tray, main window, and wires
ProcessMonitor + SaveScheduler threads to the UI via Qt signals.
"""

import logging
import sys
from logging.handlers import RotatingFileHandler
from pathlib import Path

from PySide6.QtWidgets import QApplication

from src.core.config import get_config_path, load_config, save_config
from src.core.monitor import ProcessMonitor
from src.core.scheduler import SaveScheduler
from src.ui.main_window import MainWindow
from src.ui.tray import SystemTray


def setup_logging() -> None:
    log_path = get_config_path().parent / "autosaver.log"
    handler = RotatingFileHandler(
        str(log_path), maxBytes=1_000_000, backupCount=1, encoding="utf-8"
    )
    handler.setFormatter(logging.Formatter(
        "%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    ))
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)


class App:
    def __init__(self):
        self._config = load_config()
        self._monitor = ProcessMonitor()
        self._scheduler = SaveScheduler()
        self._window = MainWindow(self._config)
        self._tray = SystemTray()
        self._tray.show()

        self._wire_signals()
        self._start_threads()

    def _wire_signals(self) -> None:
        self._tray.show_requested = self._show_window
        self._tray.pause_toggled = self._toggle_pause
        self._tray.quit_requested = self._quit

        self._monitor.status_changed.connect(self._on_status_changed)
        self._scheduler.save_done.connect(self._on_save_done)

        self._window.program_added = self._on_program_added
        self._window.program_edited = self._on_program_edited
        self._window.program_deleted = self._on_program_deleted
        self._window.pause_toggled = self._on_pause_toggled

    def _start_threads(self) -> None:
        self._monitor.configure(
            self._config.get("programs", []),
            self._config.get("global", {}).get("check_interval_sec", 3),
        )
        for prog in self._config.get("programs", []):
            if prog.get("enabled", True):
                self._scheduler.add_program(prog)
        self._monitor.start()
        self._scheduler.start()

    def _show_window(self) -> None:
        self._window.show()
        self._window.raise_()
        self._window.activateWindow()

    def _toggle_pause(self) -> None:
        if self._scheduler.is_paused:
            self._scheduler.resume_all()
            self._tray.set_paused_state(False)
            self._window.set_paused_state(False)
        else:
            self._scheduler.pause_all()
            self._tray.set_paused_state(True)
            self._window.set_paused_state(True)

    def _quit(self) -> None:
        self._scheduler.stop_scheduler()
        self._monitor.stop_monitor()
        self._scheduler.wait(3000)
        self._monitor.wait(3000)
        QApplication.quit()

    def _on_status_changed(self, program_id: str, running: bool) -> None:
        self._window.update_program_status(program_id, running)
        self._scheduler.set_running(program_id, running)

    def _on_save_done(self, program_id: str, timestamp: str, window_count: int) -> None:
        self._window.update_last_save(program_id, timestamp, window_count)

    def _on_program_added(self, prog: dict) -> None:
        self._scheduler.add_program(prog)
        self._save_config()

    def _on_program_edited(self, prog: dict) -> None:
        self._scheduler.update_program(prog)
        self._save_config()

    def _on_program_deleted(self, program_id: str) -> None:
        self._scheduler.remove_program(program_id)
        self._save_config()

    def _on_pause_toggled(self, paused: bool) -> None:
        if paused:
            self._scheduler.pause_all()
            self._tray.set_paused_state(True)
        else:
            self._scheduler.resume_all()
            self._tray.set_paused_state(False)

    def _save_config(self) -> None:
        save_config(self._config)


def main() -> None:
    setup_logging()
    logging.info("AutoSaver starting")

    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)

    auto_saver = App()

    try:
        sys.exit(app.exec())
    finally:
        save_config(auto_saver._config)
        logging.info("AutoSaver exiting")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Commit**

```bash
git add src/main.py
git commit -m "feat: add entry point with signal wiring and app lifecycle"
```

---

### Task 9: requirements.txt + build.bat

**Files:**
- Create: `requirements.txt`
- Create: `build.bat`

- [ ] **Step 1: Write requirements.txt**

```txt
PySide6>=6.5.0
pywin32>=305
psutil>=5.9.0
pyinstaller>=6.0.0
```

- [ ] **Step 2: Write build.bat**

```bat
@echo off
pyinstaller --onedir --windowed --name autosaver --icon assets/icon.ico src/main.py
echo Build complete. Output in dist\autosaver\
pause
```

- [ ] **Step 3: Create assets directory with placeholder**

```bash
mkdir -p assets
```

- [ ] **Step 4: Commit**

```bash
git add requirements.txt build.bat assets/
git commit -m "feat: add dependencies, build script, and assets directory"
```

---

## Implementation Order

```
Phase 1: Task 1 + Task 2 in parallel (foundation, no deps)
Phase 2: Task 3 + Task 4 in parallel (core, depends on foundation)
Phase 3: Task 5 + Task 6 + Task 7 in parallel (UI, depends on core)
Phase 4: Task 8 (wiring, depends on UI)
Phase 5: Task 9 (build/config, no deps)
```

## Post-Implementation Checklist

- [ ] User places `assets/icon.ico` before building
- [ ] Run `pip install -r requirements.txt` on Windows
- [ ] Run `python src/main.py` to smoke test
- [ ] Run `build.bat` to verify packaging
- [ ] Test: add a program, verify it appears in tray
- [ ] Test: Ctrl+S sends to target window
- [ ] Test: close minimizes to tray
- [ ] Test: config.json persists across restarts
