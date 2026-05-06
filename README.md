# AutoSaver

> Windows system tray app that automatically saves your work in SAI2, Photoshop, and other drawing applications.

AutoSaver monitors target processes and sends `Ctrl+S` to their windows at configurable intervals — without stealing focus. It runs quietly in the system tray, showing the status of each monitored program at a glance.

## Features

- **Focus-safe saving** — uses `PostMessage` to inject keystrokes without interrupting your workflow
- **Multi-window aware** — saves all visible windows of a target program simultaneously
- **Dynamic tray menu** — right-click shows ● (running) / ○ (stopped) for each program
- **Two add methods** — browse a local `.exe` or pick from currently running processes with search
- **Per-program intervals** — each program has its own save frequency (1 min to 60 hours)
- **Dark / Light / System theme** — follows your Windows theme preference, toggle in settings
- **Zero dependencies** — single `.exe`, no runtime install required
- **INI config** — human-readable `autosaver.ini` next to the executable

## System Requirements

- Windows 10 or later (Windows 11 recommended)
- .NET Framework 4.8 (pre-installed on Windows 10+)

## Installation

Download `autosaver.exe` from the [latest release](../../releases/latest) and place it anywhere you like.  
It creates `autosaver.ini` and `autosaver.log` in the same directory on first run.

## Build from Source

```bat
:: 1. Install Visual Studio Build Tools 2022
::    https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022
::
:: 2. Open "Developer Command Prompt for VS 2022" and run:

msbuild AutoSaver.csproj /p:Configuration=Release

:: Output: bin\Release\autosaver.exe
```

Or open `AutoSaver.csproj` in Visual Studio 2022 / VS Code with C# Dev Kit and build from there.

## Usage

1. Launch `autosaver.exe` — the tray icon appears
2. Double-click the tray icon to open the main window
3. Click **+ 添加** → browse for a `.exe` or pick from running processes
4. Set the save interval (default 5 minutes)
5. AutoSaver sends `Ctrl+S` to all visible windows of that program

**Right-click the tray icon** to see program status, open settings, or quit.

## Configuration

All settings are stored in `autosaver.ini`:

```ini
[global]
start_with_windows = false
check_interval_sec = 3
minimize_to_tray_on_close = true
theme = system

[programs]
count = 1

[program.1]
id = 550e8400-e29b-41d4-a716-446655440000
name = SAI2
exe = sai2.exe
enabled = true
save_interval_sec = 300
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language | C# 7.3 |
| Runtime | .NET Framework 4.8 |
| UI | WPF (Windows Presentation Foundation) |
| Tray | System.Windows.Forms.NotifyIcon |
| Config | kernel32 INI API (`GetPrivateProfileString`) |
| Keystroke | P/Invoke `PostMessage` (user32.dll) |
| Process | `System.Diagnostics.Process` |
| Build | MSBuild / GitHub Actions |

Zero NuGet packages. Zero runtime downloads.

## License

MIT © 2026 Normalight
