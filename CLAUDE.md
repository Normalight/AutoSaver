# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

AutoSaver is a Windows desktop app that monitors drawing applications (SAI2, Photoshop, etc.) and automatically sends `Ctrl+S` to their windows at configurable intervals. It runs as a system tray application.

## Tech stack

- Python 3.11+
- PySide6 (GUI and Qt signals for thread communication)
- pywin32 for Windows API (window enumeration, `PostMessage`-based keystroke injection)
- psutil for process enumeration
- PyInstaller (`--onedir`) for distribution

## Build / run

```bash
# Install dependencies
pip install -r requirements.txt

# Run during development
python src/main.py

# Package for distribution
pyinstaller --onedir --windowed --name autosaver --icon assets/icon.ico src/main.py
```

## Architecture

```
src/
├── main.py              # Entry point: QApplication + system tray
├── core/
│   ├── config.py        # Read/write config.json (atomic write via temp+rename)
│   ├── monitor.py       # ProcessMonitor(QThread): polls for target exe presence
│   ├── scheduler.py     # SaveScheduler(QThread): per-program save timers
│   └── saver.py         # Window enumeration + PostMessage-based Ctrl+S
└── ui/
    ├── main_window.py   # Program list with status/interval/actions columns
    ├── add_dialog.py    # Add/edit program dialog with running-process picker
    └── tray.py          # System tray icon and context menu
```

## Data flow

1. `ProcessMonitor` (background thread) polls processes every `check_interval_sec` seconds; emits `status_changed(program_id, running: bool)` when a target exe starts or stops.
2. `SaveScheduler` (one per enabled program) triggers every `save_interval_sec`; calls `saver.get_windows_by_exe(exe)` to find all visible top-level windows belonging to the target process, then calls `saver.send_ctrl_s(hwnd)` on each.
3. UI (PySide6 main thread) receives Qt signals from monitor/scheduler threads to update program status indicators and "last saved" display.

Thread communication uses Qt signals exclusively — no shared mutable state.

## Key design decisions

- `Ctrl+S` is sent via `PostMessage(WM_KEYDOWN/UP)` so it never steals focus from the user.
- `config.json` lives next to the exe; first-run auto-creates defaults.
- On close, the window minimizes to tray (if `minimize_to_tray_on_close` is enabled) rather than quitting.
- Startup-with-Windows is implemented via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key.
- Logging uses Python's `RotatingFileHandler` (1 MB max, 1 backup) to `autosaver.log`.

## Config file format

```json
{
  "global": {
    "start_with_windows": false,
    "check_interval_sec": 3,
    "minimize_to_tray_on_close": true
  },
  "programs": [
    {
      "id": "uuid",
      "name": "SAI2",
      "exe": "sai2.exe",
      "enabled": true,
      "save_interval_sec": 300
    }
  ]
}
```

## Full design spec

See `docs/superpowers/specs/2026-05-06-autosaver-design.md` for the complete design document including UI mockups, edge case handling, and module API details.
