using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AutoSaver.Models;
using AutoSaver.Services;
using Microsoft.Win32;

namespace AutoSaver.Views
{
    public partial class MainWindow : Window
    {
        private List<ProgramItem> _programs;
        private readonly Dictionary<string, string> _statuses = new Dictionary<string, string>();
        private readonly Dictionary<string, Tuple<string, int>> _lastSaves = new Dictionary<string, Tuple<string, int>>();

        public event Action<ProgramItem> ProgramAdded;
        public event Action<ProgramItem> ProgramEdited;
        public event Action<string> ProgramDeleted;

        public MainWindow(List<ProgramItem> programs)
        {
            InitializeComponent();
            _programs = programs;
            StartupCheck.IsChecked = ConfigService.StartWithWindows;
            TrayCloseCheck.IsChecked = ConfigService.MinimizeToTrayOnClose;
            RefreshList();
        }

        public void RefreshList()
        {
            var displayItems = _programs.Select(p => new ProgramDisplay
            {
                Id = p.Id,
                Name = p.Name,
                Exe = p.Exe,
                IntervalText = (p.SaveIntervalSec / 60) + " 分钟",
                StatusText = _statuses.TryGetValue(p.Id, out var running) && running == "running"
                    ? "● 运行中" : "○ 未检测到",
                StatusColor = _statuses.TryGetValue(p.Id, out var s) && s == "running"
                    ? "Green" : "Gray"
            }).ToList();

            ProgramListView.ItemsSource = displayItems;
            UpdateStatusBar();
        }

        public void UpdateProgramStatus(string programId, bool running)
        {
            _statuses[programId] = running ? "running" : "stopped";
            RefreshList();
        }

        public void UpdateLastSave(string programId, string timestamp, int windowCount)
        {
            var prog = _programs.FirstOrDefault(p => p.Id == programId);
            if (prog != null)
            {
                _lastSaves[prog.Name] = Tuple.Create(timestamp, windowCount);
                UpdateStatusBar();
            }
        }

        private void UpdateStatusBar()
        {
            if (_lastSaves.Count > 0)
            {
                var parts = _lastSaves.Select(kv => $"{kv.Key} {kv.Value.Item1} ({kv.Value.Item2}窗口)");
                StatusLabel.Text = "上次保存: " + string.Join(", ", parts);
            }
            else
            {
                StatusLabel.Text = "";
            }
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new AddEditDialog();
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _programs.Add(dlg.Result);
                ConfigService.SavePrograms(_programs);
                RefreshList();
                ProgramAdded?.Invoke(dlg.Result);
            }
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement el && el.Tag is ProgramDisplay display)) return;
            var prog = _programs.FirstOrDefault(p => p.Id == display.Id);
            if (prog == null) return;

            var dlg = new AddEditDialog(prog);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                var idx = _programs.FindIndex(p => p.Id == prog.Id);
                if (idx >= 0) _programs[idx] = dlg.Result;
                ConfigService.SavePrograms(_programs);
                RefreshList();
                ProgramEdited?.Invoke(dlg.Result);
            }
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement el && el.Tag is ProgramDisplay display)) return;
            _programs.RemoveAll(p => p.Id == display.Id);
            // Clean up last-save info for the deleted program
            var deadName = _lastSaves.Keys.FirstOrDefault(k =>
                !_programs.Any(p => p.Name == k));
            if (deadName != null) _lastSaves.Remove(deadName);
            ConfigService.SavePrograms(_programs);
            RefreshList();
            ProgramDeleted?.Invoke(display.Id);
        }

        private void OnStartupChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                var enabled = StartupCheck.IsChecked == true;
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (enabled)
                        key?.SetValue("AutoSaver", System.Reflection.Assembly.GetEntryAssembly().Location);
                    else
                        key?.DeleteValue("AutoSaver", false);
                }
                ConfigService.StartWithWindows = enabled;
            }
            catch
            {
                StartupCheck.IsChecked = !StartupCheck.IsChecked;
                MessageBox.Show("注册表写入失败，请以管理员身份运行。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTrayCloseChanged(object sender, RoutedEventArgs e)
        {
            ConfigService.MinimizeToTrayOnClose = TrayCloseCheck.IsChecked == true;
        }
    }
}
