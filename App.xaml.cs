using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using AutoSaver.Models;
using AutoSaver.Services;
using AutoSaver.Views;
using MenuItem = System.Windows.Forms.MenuItem;

namespace AutoSaver
{
    public partial class App : Application
    {
        private NotifyIcon _tray;
        private ProcessMonitor _monitor;
        private SaveScheduler _scheduler;
        private List<ProgramItem> _programs;
        private MainWindow _mainWindow;
        private string _logPath;
        private string _iconTempPath;

        public static string Version { get; private set; } = "1.0.0";

        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Read version from VERSION file
            try
            {
                var versionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                if (File.Exists(versionPath))
                    Version = File.ReadAllText(versionPath).Trim();
            }
            catch { }

            // Theme must be first - applies to all windows
            ThemeService.InitTheme(this);

            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosaver.log");
            Log($"AutoSaver v{Version} starting");

            // Extract embedded icon to temp file for NotifyIcon
            ExtractIcon();

            _programs = ConfigService.LoadPrograms();

            _monitor = new ProcessMonitor();
            _monitor.StatusChanged += OnStatusChanged;

            _scheduler = new SaveScheduler();
            _scheduler.SaveDone += OnSaveDone;

            foreach (var prog in _programs)
                _scheduler.AddProgram(prog);

            _monitor.RefreshPrograms(_programs);
            _monitor.Start(ConfigService.CheckIntervalSec);

            SetupTray();
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
                _tray.Icon = Icon.FromHandle(new Bitmap(_iconTempPath).GetHicon());
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
            base.OnExit(e);
        }

        private void Log(string message)
        {
            try
            {
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{message}]";
                File.AppendAllText(_logPath, entry + Environment.NewLine);
            }
            catch { }
        }
    }
}
