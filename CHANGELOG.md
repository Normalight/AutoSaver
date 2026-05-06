# Changelog

All notable changes to AutoSaver are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.7] - 2026-05-06

### Fixed
- **CI / MSBuild (.NET Framework 4.8)**: `string.Contains` has no `StringComparison` overload on netfx — `UpdateService` proxy URL check now uses `IndexOf` (**CS1501** on Windows runners).

## [1.5.6] - 2026-05-06

### Fixed
- **CI / MSBuild**: removed use of `SecurityProtocolType.Tls13` in `UpdateService` static ctor — some GitHub Actions / reference assemblies only expose up to TLS 1.2 in the enum, causing **CS0117** (`Tls13` does not exist).

## [1.5.5] - 2026-05-06

### Changed
- **Updates / downloads**: GitHub API and installer downloads now honor **system proxy** (`WebRequest.GetSystemWebProxy`) and **`HTTPS_PROXY` / `HTTP_PROXY` / `ALL_PROXY`** environment variables; TLS 1.2+ enabled; clearer HTTP error bodies on failure.
- **Auto-save input**: Saving uses **`PostMessage`-only Ctrl+S** (no `SendKeys`, activation, or `ShowWindow`). Removed **`BringToFront`** / `SetForegroundWindow` from the codebase.
- **Save countdown**: Single global countdown — **does not reset** when switching monitored apps or HWNDs; **pauses** when the foreground app is not on the whitelist; **pauses the 1s timer** when the main window is closed (tray only); state cleared on exit via `StopAll`.
- **Save eligibility**: If foreground no longer matches immediately before sending keys (e.g. race after `Task.Run`), the attempt is **skipped silently** with no success toast — next interval retries.

### Removed
- Notification **“jump to window”** action on save-dialog prompts (`JumpAction` removed).

## [1.5.4] - 2026-05-06

### Fixed
- **Countdown reset on in-app dialogs** (e.g. Adobe stamp/sign modals): only a change of **monitored program entry** resets the timer; the same executable may move foreground across multiple HWNDs without resetting (modal/tool windows).
- **Default config after MSI-style install**: `autosaver.ini` is now stored under **`%AppData%\AutoSaver\`** (writable when the exe lives in Program Files). Legacy `autosaver.ini` beside the portable exe is **migrated once** to AppData when present. `EnsureDefaults` creates the directory before extracting the embedded template.

## [1.5.3] - 2026-05-06

### Fixed
- **Version label showed `v0.0.0`**: classic projects were not embedding assembly version from the `.csproj` MSBuild properties alone — added `Properties/AssemblyInfo.cs` and `GetAssemblyVersion()` now prefers `AssemblyInformationalVersion`, then falls back to reading the deployed `VERSION` file beside the exe.
- **Process picker**: selected row now has visible highlight (accent border + background); each row shows **friendly name** and **exe filename** (e.g. product name + `sai.exe`) so it matches what gets saved.
- **Name mismatch after pick from running**: adding or editing from the running-process picker now keeps the **friendly display name** as `ProgramItem.Name` (same as shown in the picker), while `Exe` stays the real executable name.

## [1.5.2] - 2026-05-06

### Changed
- Scheduling is **foreground-only**: configured programs act as a whitelist; the countdown runs only while that executable owns the foreground window; **switching focus resets** the timer to the full interval.
- Removed **ProcessMonitor** (no periodic scan of all processes). Tray menu lists configured programs by **name only** (no run-state dots).

### Removed
- Per-window HWND enumeration in the scheduler, **expandable multi-window** groups in the main list, and related UI.

## [1.5.1] - 2026-05-06

### Fixed
- CI / MSBuild failed with **CS0104**: ambiguous `Application` in `WindowService` (`System.Windows.Forms.Application` vs `System.Windows.Application`). Dispatch for `SendCtrlSToWindows` now uses `System.Windows.Application.Current` explicitly.

## [1.5.0] - 2026-05-06

### Added
- Per top-level window **HWND** countdown and save targeting: each visible window has its own timer; **Ctrl+S** is sent only when that window is foreground.
- Main list **expandable groups** when a monitored executable has multiple windows; child cards show **product name · window title** and per-window countdown text.
- **PID snapshot cache** (short TTL) plus **time-throttled** full window enumeration to reduce `Process.GetProcesses` / `EnumWindows` work each tick.
- Foreground **transition** handling: switching to a window whose countdown already hit zero triggers save and reset.
- **Async** post-save dialog detection (`Task.Delay` on thread pool) so the timer callback is not blocked.

### Changed
- Program cards and title-bar capsule use the **short exe stem** (e.g. `sai2`) as the primary label; long **FileDescription** / product strings appear only under each window row or the single-window subtitle.
- **One config entry per exe**: duplicates in `autosaver.ini` are merged on load (first wins); adding the same exe again is rejected with a clear message.
- **Stable HWND ownership** when duplicate exe entries could exist: first program in sort order keeps a HWND slot.

### Fixed
- Timer thread no longer sleeps during save follow-up; reduces risk of stalled ticks under load.

## [1.4.0] - 2026-05-06

### Added
- About dialog with version info, update check, and release notes viewer.
- UpdateService — checks GitHub Releases for new versions and downloads the installer.
- Inno Setup installer with auto-built `autosaver-setup.exe`.
- Portal portable zip artifact for no-install deployment.
- ExecutableMetadataService for reading file version and icon from .exe files.

### Changed
- CI workflow now resolves Inno Setup path from both `Program Files` and `Program Files (x86)`.
- `.claude/` directory added to `.gitignore`.

### Fixed
- About dialog chrome now has rounded corners consistent with the rest of the app.
- Installer asset name centralized in CI workflow to avoid mismatches.

## [1.3.7] - 2026-05-07

### Fixed
- Notification overlay now centers correctly on high-DPI displays (125 %, 150 %, 200 % scaling). Previously used `Screen.WorkingArea` (physical pixels) to position a WPF window (logical pixels), causing the overlay to appear off-center at non-100 % DPI.
- "Jump to window" button now reliably brings the target application to the foreground. Added `AllowSetForegroundWindow` before `SetForegroundWindow` to grant the target process permission to take focus, and reordered the hide/jump sequence so AutoSaver releases the foreground before the jump fires.

## [1.3.6] - 2026-05-07

### Added
- Countdown capsule in the title bar showing time remaining until the next auto-save.

### Changed
- List item selection highlight changed from solid fill to accent-colored border style.
- Main window and dialogs now use rounded corners throughout.
- Version number is now written into `autosaver.ini` under `[meta]` on startup.

### Fixed
- Countdown capsule hides and timer stops correctly when the countdown reaches zero.
- Duplicate version entry in the default `autosaver.ini` template corrected.

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
