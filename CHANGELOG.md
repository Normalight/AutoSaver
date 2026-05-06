# Changelog

All notable changes to AutoSaver are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.5] - 2026-05-06

### Fixed
- Dark mode: ComboBox text now correctly uses theme foreground color instead of black.
- Adding a program no longer creates duplicate entries in the list.
- Process picker now has title bar, close button, cancel/confirm buttons, keyboard Enter support, and proper selection highlight.
- Process icons now use `QueryFullProcessImageName` as fallback when `MainModule` fails, fixing missing icons.
- Notification overlay reliably centers at top of screen.

### Changed
- **Unified save timer**: replaced per-program timers with a single global timer that saves programs sequentially.
- Program cards now show the executable's icon.

## [1.3.4] - 2026-05-06

### Changed
- **Compact vertical layout** — main window reduced to 300×480, child dialogs proportionally shrunk.
- Per-program save interval removed; all programs now share a single global interval.
- Settings dialog redesigned with card-based sections.
- Theme toggle buttons (dark / light / system) in settings.

### Added
- **Process picker** — add programs by selecting from currently running windowed apps with icons.
- Default `autosaver.ini` embedded and extracted on first run if missing.
- Inline enable/disable toggle button per program card.
- SVG icon buttons on the main toolbar.

### Fixed
- ComboBox background now matches the active theme.
- Scrollbar style updated for consistency.
- Modal dialogs (AddEdit, ProcessPicker) now use `ShowDialog()` correctly.
- Build failure caused by unescaped double quotes in interpolated string.

## [1.3.3] - 2026-05-06

### Fixed
- Main window now opens automatically on startup instead of hiding to the system tray.

## [1.3.2] - 2026-05-06

### Changed
- Version is now stored in `autosaver.ini` under `[meta]` instead of a separate `VERSION` file.
- Settings dialog height reduced (640→480) for a more compact layout.

### Fixed
- `AddEditDialog` round corners no longer show white background corners — window now uses `AllowsTransparency="True"`.
- Input fields (`NameBox`, `ExeBox`) in `AddEditDialog` now display text correctly with explicit height instead of margin-based sizing.

## [1.3.1] - 2026-05-06

### Added
- Single-instance enforcement via named mutex — launching a second process exits immediately.
- Assembly version attributes (`AssemblyVersion`, `FileVersion`, `AssemblyInformationalVersion`) in project file.

### Changed
- Version fallback now uses assembly version when the `VERSION` file is missing or empty.
- Settings dialog: increased card spacing, wider minimum size, footer anchored in a separated border.
- Title-bar height and margins slightly increased on settings window for better visual balance.

### Fixed
- ComboBox dropdown and selected item now use full theme templates in both dark and light mode, ensuring text vertical centering.
- System title-bar remnants removed on `MainWindow` and `SettingsDialog` via explicit `WindowChrome`.
- `AddEditDialog`, `ProcessPickerDialog`, and `NotificationOverlay` no longer show default OS chrome — all windows use `WindowStyle="None"` with themed borders.
- `NotificationOverlay` resource references changed from `StaticResource` to `DynamicResource` for proper theme switching.

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
