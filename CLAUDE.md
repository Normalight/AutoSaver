# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project overview

AutoSaver is a Windows system tray app that monitors drawing applications (SAI2, Photoshop, etc.) and automatically sends `Ctrl+S` to their windows via `PostMessage` at configurable intervals. No focus stealing, multi-window aware.

## Tech stack

- C# 7.3 / .NET Framework 4.8
- WPF (UI), System.Windows.Forms.NotifyIcon (tray)
- P/Invoke (user32.dll for window enumeration + PostMessage, kernel32.dll for INI read/write)
- Zero NuGet dependencies, zero runtime install (~300KB exe)
- MSBuild for compilation (Visual Studio Build Tools 2022 or full VS)
- GitHub Actions for CI/CD (windows-latest runner)

## Build / run

```bat
:: One-time: install Visual Studio Build Tools 2022
:: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022

:: Build from Developer Command Prompt for VS 2022:
msbuild AutoSaver.csproj /p:Configuration=Release

:: Output: bin\Release\autosaver.exe (copy + generated-image-2.png to distribute)
```

No `dotnet build` — this is .NET Framework 4.8, not .NET Core. Must use MSBuild on Windows.

## Architecture

```
AutoSaver/
├── App.xaml/.cs              # Entry: theme init, tray setup, signal wiring, icon extraction
├── VERSION                   # Single source of truth for version number (SemVer)
├── CHANGELOG.md              # Release notes per version
├── Models/
│   ├── ProgramItem.cs        # Domain model: Id, Name, Exe, Enabled, SaveIntervalSec
│   └── ProgramDisplay.cs     # View model: adds StatusText, StatusColor (Brush), IntervalText
├── Services/
│   ├── ConfigService.cs      # INI read/write via kernel32 P/Invoke
│   ├── ThemeService.cs       # Dark/Light/System theme detection and switching
│   ├── ProcessMonitor.cs     # DispatcherTimer polls Process.GetProcesses()
│   ├── SaveScheduler.cs      # Per-program System.Timers.Timer → WindowService
│   └── WindowService.cs      # P/Invoke: EnumWindows + PostMessage Ctrl+S
├── Views/
│   ├── MainWindow.xaml/.cs   # Program list, create/destroy per show cycle
│   ├── AddEditDialog.xaml/.cs # Two add methods: browse exe + pick running process
│   ├── ProcessPickerDialog.xaml/.cs # Running process list with search filter
│   └── SettingsDialog.xaml/.cs # Theme, interval, startup, tray settings
├── Themes/
│   ├── DarkTheme.xaml        # Dark theme resource dictionary (#121214 base)
│   └── LightTheme.xaml       # Light theme resource dictionary (#F5F5F8 base)
├── Resources/
│   └── app-icon.png          # Embedded resource, extracted to %TEMP% at runtime
└── .github/workflows/
    └── build.yml             # CI: build on push/PR, release on tag
```

## Data flow

1. `ProcessMonitor` (DispatcherTimer, UI thread) polls `Process.GetProcesses()` every `check_interval_sec`; emits `StatusChanged(ProgramItem, bool)` when running state changes.
2. `SaveScheduler` (one `System.Timers.Timer` per program, thread pool) triggers at `save_interval_sec`; calls `WindowService.GetWindowsByExe()` → `SendCtrlS()` on each HWND.
3. `App.xaml.cs` wires signals: Monitor → UI updates + Scheduler start/stop; Scheduler → UI last-save display.
4. `MainWindow` created with `new MainWindow(programs)` on tray double-click, destroyed on close (`Closed` → `_mainWindow = null`).

**Thread safety:** `DispatcherTimer` callbacks are on UI thread. `System.Timers.Timer` callbacks (`DoSave`) run on thread pool — use `Dispatcher.Invoke()` for UI updates. `SaveScheduler` uses `lock(_lock)` to protect `_timers` dictionary.

## Key design decisions

- `ShutdownMode="OnExplicitShutdown"` in App.xaml — closing the window does NOT quit the app.
- Icon embedded as `EmbeddedResource` in .csproj, extracted to `%TEMP%\autosaver_icon.png` at startup, cleaned up on exit.
- Themes loaded via `ResourceDictionary.MergedDictionaries` swap; `ThemeService` detects system theme from registry `AppsUseLightTheme`.
- `ProcessMonitor.RefreshPrograms` creates a defensive copy of the programs list.

## Version management (MANDATORY)

**These rules are binding. Do not skip them.**

### When changing version:
1. The **single source of truth** for the version number is the `VERSION` file at the repo root. Use [SemVer](https://semver.org/): `MAJOR.MINOR.PATCH`.
2. When bumping the version, update **ALL** of these files to match:
   - `VERSION` — the version number only (e.g., `1.0.0`)
   - `CHANGELOG.md` — add a new `## [X.Y.Z] - YYYY-MM-DD` section at the top, following the existing format
   - `README.md` — if version referenced in text (e.g., badges)

### When creating a release:
1. Update `VERSION` and `CHANGELOG.md` in a commit with message `release: vX.Y.Z`
2. Create an annotated Git tag: `git tag -a vX.Y.Z -m "Release vX.Y.Z"`
3. Push the tag: `git push origin vX.Y.Z`
4. GitHub Actions will automatically build and create a GitHub Release with `autosaver.exe` attached

### CHANGELOG format:
- Follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
- Sections: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`
- Each entry is a bullet point describing the change from the user's perspective

## Logging

Simple `File.AppendAllText` to `autosaver.log` next to the exe. Format: `yyyy-MM-dd HH:mm:ss [message]`. No log rotation in the C# version (kept simple; users can delete the file to reset).
