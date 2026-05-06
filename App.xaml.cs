using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AutoSaver.Models;
using AutoSaver.Services;
using AutoSaver.Views;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace AutoSaver
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "AutoSaver.SingleInstance";
        private static Mutex _singleInstanceMutex;
        private NotifyIcon _tray;
        private SaveScheduler _scheduler;
        private List<ProgramItem> _programs;
        private MainWindow _mainWindow;
        private NotificationOverlay _notification;
        private readonly Queue<SaveResult> _notificationQueue = new Queue<SaveResult>();
        private bool _notificationShowing;
        private string _logPath;
        private string _iconTempPath;
        private UpdateCheckResult _lastUpdateCheckResult;
        private bool _isCheckingUpdates;

        public static string Version { get; private set; } = GetAssemblyVersion();
        public static string CurrentReleaseNotes { get; private set; } = "";
        public static string RepositoryUrl => "https://github.com/Normalight/AutoSaver";

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var createdNew = false;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            ConfigService.EnsureDefaults();

            var assemblyVersion = GetAssemblyVersion();
            Version = assemblyVersion;
            var iniVersion = ConfigService.AppVersion;
            if (string.IsNullOrWhiteSpace(iniVersion))
                ConfigService.AppVersion = Version;

            CurrentReleaseNotes = ChangelogService.GetReleaseNotes(changelogPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md"), version: Version);
            _lastUpdateCheckResult = new UpdateCheckResult
            {
                CurrentVersion = Version,
                ReleaseNotes = CurrentReleaseNotes
            };

            // Theme must be first - applies to all windows
            ThemeService.InitTheme(this);

            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosaver.log");
            Log($"AutoSaver v{Version} starting");
            Log($"Release notes loaded for v{Version}: {CurrentReleaseNotes.Length} characters");

            // Extract embedded icon to temp file for NotifyIcon
            ExtractIcon();

            _programs = ConfigService.LoadPrograms();

            _scheduler = new SaveScheduler();
            _scheduler.SaveDone += OnSaveDone;
            _scheduler.SaveCompleted += OnSaveCompleted;
            _scheduler.FocusCountdown += OnFocusCountdown;
            _scheduler.ProgramListTick += OnProgramListTick;

            foreach (var prog in _programs)
                _scheduler.AddProgram(prog);

            _scheduler.SetInterval(ConfigService.CheckIntervalSec);
            _scheduler.Start();

            SetupTray();
            ShowMainWindow();

            if (ConfigService.CheckUpdatesOnStartup)
                BeginBackgroundUpdateCheck();
        }

        public static string GetFallbackReleaseNotes(string version)
        {
            return ChangelogService.GetReleaseNotes(
                changelogPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md"),
                version: version);
        }

        private static string GetAssemblyVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private void ExtractIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("AutoSaver.Resources.app-icon.png"))
                {
                    if (stream != null)
                    {
                        _iconTempPath = Path.Combine(Path.GetTempPath(), "autosaver_icon.png");
                        using (var fs = new FileStream(_iconTempPath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(fs);
                        }
                        Log("Icon extracted to: " + _iconTempPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Icon extraction failed: " + ex.Message);
            }
        }

        private void SetupTray()
        {
            _tray = new NotifyIcon
            {
                Text = "AutoSaver",
                Visible = true
            };

            // Use extracted icon, fallback to system default
            if (!string.IsNullOrEmpty(_iconTempPath) && File.Exists(_iconTempPath))
            {
                using (var bmp = new Bitmap(_iconTempPath))
                    _tray.Icon = Icon.FromHandle(bmp.GetHicon());
            }
            else
                _tray.Icon = SystemIcons.Application;

            _tray.DoubleClick += (s, ev) => ShowMainWindow();
            _tray.ContextMenuStrip = new ContextMenuStrip();
            _tray.ContextMenuStrip.Opening += (s, ev) => RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            var menu = _tray.ContextMenuStrip;
            menu.Items.Clear();

            foreach (var prog in _programs)
                menu.Items.Add(new ToolStripMenuItem(prog.Name) { Enabled = false });

            if (_programs.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("显示主窗口", null, (s, e) => ShowMainWindow()));
            menu.Items.Add(new ToolStripMenuItem("设置", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => QuitApp()));
        }

        private void ShowMainWindow()
        {
            if (_mainWindow != null)
            {
                _mainWindow.Activate();
                _mainWindow.WindowState = WindowState.Normal;
                return;
            }

            _mainWindow = new MainWindow(_programs);

            _mainWindow.ProgramAdded += OnProgramAdded;
            _mainWindow.ProgramDeleted += OnProgramDeleted;
            _mainWindow.SettingsSaved += OnMainWindowSettingsSaved;

            _mainWindow.Closed += (s, e) =>
            {
                _mainWindow.SettingsSaved -= OnMainWindowSettingsSaved;
                _mainWindow = null;
            };

            _mainWindow.Show();
        }

        private void ShowNotification(SaveResult result)
        {
            if (_notification == null)
            {
                _notification = new NotificationOverlay();
                _notification.Hidden += OnNotificationHidden;
            }

            var type = result.Status switch
            {
                SaveStatus.Success => NotificationType.Success,
                SaveStatus.NeedsConfirm => NotificationType.NeedsConfirm,
                SaveStatus.Failed => NotificationType.Failed,
                _ => NotificationType.Success
            };

            _notificationShowing = true;
            _notification.Show(result.Program.Name, result.Message, type, result.JumpAction);
        }

        private void OnNotificationHidden()
        {
            _notificationShowing = false;
            TryShowNextNotification();
        }

        private void TryShowNextNotification()
        {
            if (_notificationShowing) return;
            if (_notificationQueue.Count == 0) return;

            ShowNotification(_notificationQueue.Dequeue());
        }

        private void EnqueueNotification(SaveResult result)
        {
            _notificationQueue.Enqueue(result);
            TryShowNextNotification();
        }

        private void ShowSettings()
        {
            var dlg = new SettingsDialog();
            dlg.Owner = _mainWindow; // may be null, that's fine
            if (dlg.ShowDialog() == true)
                ApplySavedSettings();
        }

        private void OnMainWindowSettingsSaved()
        {
            ApplySavedSettings();
        }

        private void ApplySavedSettings()
        {
            _scheduler.SetInterval(ConfigService.CheckIntervalSec);
            Log("Settings updated");
        }

        public UpdateCheckResult GetLastUpdateCheckResult()
        {
            return CloneUpdateResult(_lastUpdateCheckResult);
        }

        public bool IsCheckingUpdates()
        {
            return _isCheckingUpdates;
        }

        public void OpenAboutDialog(Window owner)
        {
            var dialog = new AboutDialog();
            dialog.Owner = owner ?? _mainWindow;
            dialog.ShowDialog();
        }

        public void OpenReleasePage(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return;

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("Open release page failed: " + ex.Message);
                MessageBox.Show("无法打开发布页。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void BeginBackgroundUpdateCheck(Action<UpdateCheckResult> onCompleted = null)
        {
            if (_isCheckingUpdates)
                return;

            _isCheckingUpdates = true;
            UpdateService.CheckAsync().ContinueWith(task =>
            {
                Dispatcher.Invoke(() =>
                {
                    _isCheckingUpdates = false;
                    var result = task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                        ? task.Result
                        : new UpdateCheckResult
                        {
                            CurrentVersion = Version,
                            ErrorMessage = task.Exception?.GetBaseException().Message ?? "检查更新失败。"
                        };
                    if (string.IsNullOrWhiteSpace(result.ReleaseNotes))
                        result.ReleaseNotes = GetFallbackReleaseNotes(result.HasUpdate ? result.LatestVersion : Version);
                    StoreUpdateCheckResult(result);
                    onCompleted?.Invoke(CloneUpdateResult(result));
                });
            });
        }

        private void StoreUpdateCheckResult(UpdateCheckResult result)
        {
            _lastUpdateCheckResult = CloneUpdateResult(result);
        }

        private static UpdateCheckResult CloneUpdateResult(UpdateCheckResult source)
        {
            if (source == null)
                return null;

            return new UpdateCheckResult
            {
                CurrentVersion = source.CurrentVersion,
                LatestVersion = source.LatestVersion,
                HasUpdate = source.HasUpdate,
                ReleaseNotes = source.ReleaseNotes,
                ReleaseUrl = source.ReleaseUrl,
                InstallerUrl = source.InstallerUrl,
                ErrorMessage = source.ErrorMessage
            };
        }

        public void DownloadAndInstallUpdate(UpdateCheckResult result, Action<long, long> onProgress, Action onSucceeded, Action<string, string> onFailed)
        {
            var latestVersion = result?.LatestVersion ?? "";
            var releaseUrl = result?.ReleaseUrl ?? RepositoryUrl + "/releases/latest";

            BeginBackgroundUpdateCheck(refreshed =>
            {
                if (refreshed == null)
                {
                    onFailed?.Invoke("检查更新失败。", releaseUrl);
                    return;
                }

                if (!refreshed.IsSuccess)
                {
                    onFailed?.Invoke(string.IsNullOrWhiteSpace(refreshed.ErrorMessage) ? "检查更新失败。" : refreshed.ErrorMessage, refreshed.ReleaseUrl);
                    return;
                }

                if (!refreshed.HasUpdate)
                {
                    onFailed?.Invoke("当前已是最新版本。", refreshed.ReleaseUrl);
                    return;
                }

                if (string.IsNullOrWhiteSpace(refreshed.InstallerUrl))
                {
                    onFailed?.Invoke("当前版本未提供可自动安装的安装器。", refreshed.ReleaseUrl);
                    return;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"AutoSaver-Setup-v{refreshed.LatestVersion}.exe");
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        UpdateService.DownloadInstaller(refreshed.InstallerUrl, tempPath, (downloaded, total) =>
                        {
                            Dispatcher.Invoke(() => onProgress?.Invoke(downloaded, total));
                        });

                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                UpdateService.LaunchInstaller(tempPath);
                                onSucceeded?.Invoke();
                                Shutdown();
                            }
                            catch (Exception ex)
                            {
                                onFailed?.Invoke(ex.Message, refreshed.ReleaseUrl);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => onFailed?.Invoke(ex.Message, refreshed.ReleaseUrl));
                    }
                });
            });
        }

        private void OnSaveDone(string programId, string timestamp, int windowCount)
        {
            Dispatcher.Invoke(() =>
            {
                _mainWindow?.UpdateLastSave(programId, timestamp, windowCount);
            });
            Log($"Saved: {programId} at {timestamp}, {windowCount} window(s)");
        }

        private void OnSaveCompleted(SaveResult result)
        {
            if (!ConfigService.ShowNotifications) return;
            Dispatcher.Invoke(() => EnqueueNotification(result));
        }

        private void OnFocusCountdown(FocusCountdownSnapshot snap)
        {
            void Apply() => _mainWindow?.UpdateFocusCountdown(snap);
            if (Dispatcher.CheckAccess())
                Apply();
            else
                Dispatcher.BeginInvoke((Action)Apply, DispatcherPriority.Normal);
        }

        private void OnProgramListTick(IReadOnlyList<ProgramListRow> rows)
        {
            void Apply() => _mainWindow?.ApplyProgramCountdowns(rows);
            if (Dispatcher.CheckAccess())
                Apply();
            else
                Dispatcher.BeginInvoke((Action)Apply, DispatcherPriority.Normal);
        }

        private void OnProgramAdded(ProgramItem prog)
        {
            Log($"Program added: {prog.Name} ({prog.Exe})");
            _programs.Add(prog);
            _scheduler.AddProgram(prog);
            ConfigService.SavePrograms(_programs);
        }

        private void OnProgramEdited(ProgramItem prog)
        {
            Log($"Program edited: {prog.Name} ({prog.Exe})");
            var idx = _programs.FindIndex(p => p.Id == prog.Id);
            if (idx >= 0) _programs[idx] = prog;
            _scheduler.UpdateProgram(prog);
            ConfigService.SavePrograms(_programs);
        }

        private void OnProgramDeleted(string programId)
        {
            Log($"Program deleted: {programId}");
            _programs.RemoveAll(p => p.Id == programId);
            _scheduler.RemoveProgram(programId);
            ConfigService.SavePrograms(_programs);
        }

        private void QuitApp()
        {
            Log("AutoSaver exiting");
            _scheduler.StopAll();
            _tray.Visible = false;
            _tray.Dispose();

            // Clean up temp icon
            try { if (_iconTempPath != null) File.Delete(_iconTempPath); }
            catch { }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _scheduler?.StopAll();
            _tray?.Dispose();
            try { if (_iconTempPath != null) File.Delete(_iconTempPath); }
            catch { }
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private static readonly object _logLock = new object();
        private const long MaxLogSize = 1_000_000;

        private void Log(string message)
        {
            try
            {
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{message}]{Environment.NewLine}";
                lock (_logLock)
                {
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxLogSize)
                    {
                        var bak = _logPath + ".bak";
                        try { File.Delete(bak); } catch { }
                        File.Move(_logPath, bak);
                    }
                    File.AppendAllText(_logPath, entry);
                }
            }
            catch { }
        }
    }
}
