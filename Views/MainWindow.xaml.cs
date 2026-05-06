using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AutoSaver.Models;
using AutoSaver.Services;

namespace AutoSaver.Views
{
    public partial class MainWindow : Window
    {
        private List<ProgramItem> _programs;
        private readonly Dictionary<string, string> _statuses = new Dictionary<string, string>();
        private readonly Dictionary<string, Tuple<string, int>> _lastSaves = new Dictionary<string, Tuple<string, int>>();
        private static readonly SolidColorBrush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        private static readonly SolidColorBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x98));

        public event Action<ProgramItem> ProgramAdded;
        public event Action<ProgramItem> ProgramEdited;
        public event Action<string> ProgramDeleted;

        public MainWindow(List<ProgramItem> programs)
        {
            InitializeComponent();
            _programs = programs;
            VersionLabel.Text = "v" + App.Version;
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
                    ? SuccessBrush : MutedBrush
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

            var result = MessageBox.Show(
                $"确定要删除 \"{display.Name}\" 吗？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _programs.RemoveAll(p => p.Id == display.Id);
            var deadName = _lastSaves.Keys.FirstOrDefault(k =>
                !_programs.Any(p => p.Name == k));
            if (deadName != null) _lastSaves.Remove(deadName);
            ConfigService.SavePrograms(_programs);
            RefreshList();
            ProgramDeleted?.Invoke(display.Id);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog();
            dlg.Owner = this;
            dlg.ShowDialog();
        }
    }
}
