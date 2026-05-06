using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
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
        private ProcessMonitor _monitor;
        private SaveScheduler _scheduler;
        private List<ProgramItem> _programs;
        private MainWindow _mainWindow;
        private NotificationOverlay _notification;
        private string _logPath;
        private string _iconTempPath;

        public static string Version { get; private set; } = GetAssemblyVersion();
        public static string CurrentReleaseNotes { get; private set; } = "";

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var createdNew = false;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            var iniVersion = ConfigService.AppVersion;
            if (!string.IsNullOrWhiteSpace(iniVersion))
                Version = iniVersion;
            else
            {
                Version = GetAssemblyVersion();
                ConfigService.AppVersion = Version;
            }

            CurrentReleaseNotes = ChangelogService.GetReleaseNotes(changelogPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md"), version: Version);

            // Theme must be first - applies to all windows
            ThemeService.InitTheme(this);

            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosaver.log");
            Log($"AutoSaver v{Version} starting");
            Log($"Release notes loaded for v{Version}: {CurrentReleaseNotes.Length} characters");

            // Extract embedded icon to temp file for NotifyIcon
            ExtractIcon();

            _programs = ConfigService.LoadPrograms();

            _monitor = new ProcessMonitor();
            _monitor.StatusChanged += OnStatusChanged;

            _scheduler = new SaveScheduler();
            _scheduler.SaveDone += OnSaveDone;
            _scheduler.SaveCompleted += OnSaveCompleted;

            foreach (var prog in _programs)
                _scheduler.AddProgram(prog);

            _monitor.RefreshPrograms(_programs);
            _monitor.Start(ConfigService.CheckIntervalSec);

            SetupTray();
            ShowMainWindow();
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
            {
                var running = _monitor.GetStatus(prog.Id);
                var text = (running ? "● " : "○ ") + prog.Name;
                menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });
            }

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

            foreach (var prog in _programs)
                _mainWindow.UpdateProgramStatus(prog.Id, _monitor.GetStatus(prog.Id));

            _mainWindow.ProgramAdded += OnProgramAdded;
            _mainWindow.ProgramEdited += OnProgramEdited;
            _mainWindow.ProgramDeleted += OnProgramDeleted;

            _mainWindow.Closed += (s, e) => { _mainWindow = null; };

            _mainWindow.Show();
        }

        private void ShowNotification(SaveResult result)
        {
            if (_notification == null)
                _notification = new NotificationOverlay();

            var type = result.Status switch
            {
                SaveStatus.Success => NotificationType.Success,
                SaveStatus.NeedsConfirm => NotificationType.NeedsConfirm,
                SaveStatus.Failed => NotificationType.Failed,
                _ => NotificationType.Success
            };

            _notification.Show(result.Program.Name, result.Message, type, result.JumpAction);
        }

        private void ShowSettings()
        {
            var dlg = new SettingsDialog();
            dlg.Owner = _mainWindow; // may be null, that's fine
            if (dlg.ShowDialog() == true)
            {
                // Theme already applied by SettingsDialog
                // Update monitor interval
                _monitor.Stop();
                _monitor.Start(ConfigService.CheckIntervalSec);
                Log("Settings updated");
            }
        }

        private void OnStatusChanged(ProgramItem prog, bool running)
        {
            Dispatcher.Invoke(() =>
            {
                _mainWindow?.UpdateProgramStatus(prog.Id, running);
                _scheduler.SetRunning(prog.Id, running);
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
            Dispatcher.Invoke(() => ShowNotification(result));
        }

        private void OnProgramAdded(ProgramItem prog)
        {
            Log($"Program added: {prog.Name} ({prog.Exe})");
            _programs.Add(prog);
            _scheduler.AddProgram(prog);
            _monitor.RefreshPrograms(_programs);
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
            _monitor.RefreshPrograms(_programs);
            ConfigService.SavePrograms(_programs);
        }

        private void QuitApp()
        {
            Log("AutoSaver exiting");
            _scheduler.StopAll();
            _monitor.Stop();
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
            _monitor?.Stop();
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
