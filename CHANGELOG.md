# Changelog

All notable changes to AutoSaver are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-05-06

### Added
- Card-based main window redesign with custom title bar controls and quick status summary.
- Redesigned settings dialog with custom title bar, grouped cards, and scrollable content.
- Release notes loading for the current app version from `CHANGELOG.md`.
- GitHub release notes extraction for the matching version section.

### Changed
- Program list now uses responsive cards instead of the old fixed-column table layout.
- Theme resources were unified across dark and light modes for consistent runtime switching.
- Default check interval is now 30 seconds.
- Settings interval input now supports seconds, minutes, and hours while storing seconds internally.
- Program executable paths are shortened in cards, with the full path available as a tooltip.

### Fixed
- Fixed theme dictionary replacement so dark, light, and system-following modes update consistently.
- Fixed settings window content being covered at smaller sizes by separating scrollable content from footer actions.
- Fixed running-count display so deleted programs no longer leave stale running status behind.
- Fixed release notes display so only the current version section is shown instead of the full changelog.

## [1.2.0] - 2026-05-06

### Added
- Desktop notification overlay with slide-in/out animations (CubicEase, 300ms/250ms)
- Three notification types: success (green, auto-dismiss 4s), needs confirmation (yellow, jump-to-window), failed (red, dismiss)
- Save As dialog detection: compares window count before/after Ctrl+S to detect popup dialogs
- BringToFront action: restores and focuses the target window from notification
- Settings toggle for save notifications (`show_notifications` in INI)

### Changed
- Programs that are not running are silently skipped (no notification)
- `SaveScheduler` emits `SaveCompleted(SaveResult)` event with status, message, window count, and jump action
- `WindowService` gains `GetAllWindowsByExe`, `GetWindowCountByExe`, `BringToFront` with `ShowWindow`/`SetForegroundWindow`
- INI config: added `show_notifications` key, removed redundant `[programs] count`

### Fixed
- Log file rotates at 1 MB (renames to `autosaver.log.bak`)
- Concurrent log writes protected by lock
- Bitmap resource properly disposed after icon extraction
- `ApplicationIcon` changed from PNG to proper ICO format (5 sizes, 16-256px)
- INI stale sections cleaned up on save

## [1.0.0] - 2026-05-06

### Added
- System tray application with dynamic right-click menu showing monitored programs and running status
- Main window with program list (name, status, interval, actions)
- Two methods to add programs: browse local `.exe` file or pick from running processes with search/filter
- Per-program configurable save interval (1-3600 minutes)
- Automatic `Ctrl+S` injection via `PostMessage` (no focus stealing, works for all visible windows)
- INI-based configuration (`autosaver.ini`) using `GetPrivateProfileString`/`WritePrivateProfileString`
- Dark, light, and system-following themes with runtime switching
- Settings dialog: theme picker, check interval, startup-with-Windows, minimize-to-tray-on-close
- Embedded app icon as managed resource, extracted to temp at runtime
- Rotating log file (`autosaver.log`, max 1 MB, 1 backup)
- Startup-with-Windows via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key

### Technical
- C# WPF (.NET Framework 4.8), zero NuGet dependencies, zero runtime install
- P/Invoke for Win32 API: `EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `PostMessage`, INI read/write
- `DispatcherTimer` for process monitoring (UI thread), `System.Timers.Timer` for save scheduling (thread pool)
- `NotifyIcon` (Windows Forms) for system tray with dynamic `ContextMenuStrip` rebuild on `Opening` event
- Main window created/destroyed per show cycle (GC-friendly)
- GitHub Actions CI/CD: build on push, create release artifact
