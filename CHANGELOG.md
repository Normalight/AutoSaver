# Changelog

All notable changes to AutoSaver are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-05-06

### Added
- Desktop notification overlay: slides in from top of screen on each auto-save event
- Three notification types: ✓ success (green, auto-dismisses 4s), ⚠ needs confirmation (yellow, with jump-to-window button), ✕ failed (red, with close button)
- Notification animations: CubicEase slide-in (300ms) and slide-out (250ms)
- Settings toggle for save notifications (`show_notifications` in INI)

### Changed
- `SaveScheduler` now emits `SaveCompleted` event with `SaveResult` (status, message, jump action)
- Zero-window saves are reported as `NeedsConfirm` instead of silently succeeding

### Changed
- Simplified INI format: removed redundant `[programs] count` key, programs loaded by scanning sections until empty
- Stale INI sections beyond current program count are now cleaned up on save

### Fixed
- Log file now rotates at 1 MB (renames to `autosaver.log.bak`) instead of growing unbounded
- Concurrent log writes protected by lock
- Bitmap resource properly disposed after icon extraction

## [1.0.0] - 2026-05-06

### Added
- System tray application with dynamic right-click menu showing monitored programs and running status (●/○)
- Main window with program list (name, status, interval, actions)
- Two methods to add programs: browse local `.exe` file or pick from running processes with search/filter
- Per-program configurable save interval (1–3600 minutes)
- Automatic `Ctrl+S` injection via `PostMessage` (no focus stealing, works for all visible windows)
- INI-based configuration (`autosaver.ini`) using `GetPrivateProfileString`/`WritePrivateProfileString`
- Dark, light, and system-following themes with runtime switching
- Settings dialog: theme picker, check interval, startup-with-Windows, minimize-to-tray-on-close
- Embedded app icon as managed resource, extracted to temp at runtime
- Rotating log file (`autosaver.log`, max 1 MB, 1 backup)
- Startup-with-Windows via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key
- `ShutdownMode=OnExplicitShutdown` so closing the window minimizes to tray instead of quitting
- Delete confirmation dialog
- Defensive list copy in `ProcessMonitor.RefreshPrograms` to avoid shared reference bugs
- `Dispatcher.Invoke` wrapping for cross-thread-safe UI updates from `SaveScheduler` timer callbacks
- Centralized `VERSION` file and CHANGELOG.md for release management

### Technical
- C# WPF (.NET Framework 4.8), zero NuGet dependencies, zero runtime install
- P/Invoke for Win32 API: `EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `PostMessage`, INI read/write
- `DispatcherTimer` for process monitoring (UI thread), `System.Timers.Timer` for save scheduling (thread pool)
- `NotifyIcon` (Windows Forms) for system tray with dynamic `ContextMenuStrip` rebuild on `Opening` event
- Main window created/destroyed per show cycle (GC-friendly)
- GitHub Actions CI/CD: build on push, create release artifact
- ~300 KB release `.exe`, copy-and-run distribution
