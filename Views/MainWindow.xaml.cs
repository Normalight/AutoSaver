using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoSaver.Models;
using AutoSaver.Services;
using Microsoft.Win32;
using System.Windows.Threading;

namespace AutoSaver.Views
{
    public partial class MainWindow : Window
    {
        private List<ProgramItem> _programs;
        private readonly Dictionary<string, string> _statuses = new Dictionary<string, string>();
        private readonly Dictionary<string, Tuple<string, int>> _lastSaves = new Dictionary<string, Tuple<string, int>>();
        private static readonly SolidColorBrush FallbackSuccessBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        private static readonly SolidColorBrush FallbackMutedBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x98));

        public event Action<ProgramItem> ProgramAdded;
        public event Action<string> ProgramDeleted;
        /// <summary>Fired when Settings dialog saves successfully (interval/theme/etc.).</summary>
        public event Action SettingsSaved;

        private DispatcherTimer _countdownTimer;
        private DateTime _nextSaveTime;

        public void SetNextSaveTime(int intervalSec)
        {
            _nextSaveTime = DateTime.Now.AddSeconds(intervalSec);
            if (_countdownTimer == null)
            {
                _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _countdownTimer.Tick += OnCountdownTick;
                _countdownTimer.Start();
            }
            CountdownCapsule.Visibility = Visibility.Visible;
        }

        private void OnCountdownTick(object sender, EventArgs e)
        {
            var remaining = _nextSaveTime - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
            {
                _countdownTimer.Stop();
                CountdownCapsule.Visibility = Visibility.Collapsed;
                return;
            }
            CountdownLabel.Text = remaining.TotalSeconds >= 60
                ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s"
                : $"{(int)remaining.TotalSeconds}s";
        }

        public MainWindow(List<ProgramItem> programs)
        {
            InitializeComponent();
            _programs = programs;
            VersionLabel.Text = "v" + App.Version;
            RefreshList();
        }

        public void RefreshList()
        {
            var selected = (ProgramListView.SelectedItem as ProgramDisplay)?.Id;

            var displayItems = _programs.Select(p =>
            {
                var isRunning = _statuses.TryGetValue(p.Id, out var running) && running == "running";
                var exePath = GetExePath(p.Exe);
                return new ProgramDisplay
                {
                    Id = p.Id,
                    Name = CreateDisplayName(p.Name, p.Exe, exePath),
                    Exe = p.Exe,
                    ExeSummary = CreateExeSummary(p.Exe),
                    StatusColor = isRunning
                        ? (TryFindResource("SuccessColor") as Brush ?? FallbackSuccessBrush)
                        : (TryFindResource("TextMuted") as Brush ?? FallbackMutedBrush),
                    Enabled = p.Enabled,
                    Icon = GetIconFromPath(exePath)
                };
            }).ToList();

            ProgramListView.ItemsSource = displayItems;

            if (selected != null)
            {
                var item = displayItems.FirstOrDefault(d => d.Id == selected);
                if (item != null) ProgramListView.SelectedItem = item;
            }

            UpdateDeleteButton();
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
                RefreshList();
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeleteButton();
        }

        private void UpdateDeleteButton()
        {
            DeleteButton.IsEnabled = ProgramListView.SelectedItem != null;
        }

        private static string CreateExeSummary(string exe)
        {
            if (string.IsNullOrWhiteSpace(exe)) return "未配置路径";
            var fileName = Path.GetFileName(exe);
            return string.IsNullOrEmpty(fileName) ? exe : fileName;
        }

        private static string CreateDisplayName(string storedName, string exe, string exePath)
        {
            var friendlyName = ExecutableMetadataService.GetFriendlyName(exePath);
            if (!string.IsNullOrWhiteSpace(friendlyName)) return friendlyName;
            if (!string.IsNullOrWhiteSpace(storedName)) return storedName;
            return Path.GetFileNameWithoutExtension(exe);
        }

        private static ImageSource GetIconFromPath(string path)
        {
            return ExecutableMetadataService.GetIcon(path);
        }

        private static string GetExePath(string exeName)
        {
            return ExecutableMetadataService.GetExePath(exeName);
        }

        private static string GetFriendlyName(string exePath)
        {
            return ExecutableMetadataService.GetFriendlyName(exePath);
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            // Show a small menu: browse file or pick running process
            var menu = new ContextMenu();

            var browseItem = new MenuItem { Header = "📁 选择本地程序" };
            browseItem.Click += (s, _) => AddByBrowse();
            menu.Items.Add(browseItem);

            var pickItem = new MenuItem { Header = "📋 从运行中选取" };
            pickItem.Click += (s, _) => AddByPicker();
            menu.Items.Add(pickItem);

            menu.PlacementTarget = (Button)sender;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen = true;
        }

        private void AddByBrowse()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe",
                Title = "选择目标程序"
            };
            try { dlg.InitialDirectory = @"C:\Program Files"; }
            catch { dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); }

            if (dlg.ShowDialog() != true) return;

            var exe = Path.GetFileName(dlg.FileName);
            var name = GetFriendlyName(dlg.FileName)
                       ?? Path.GetFileNameWithoutExtension(dlg.FileName);
            AddProgram(name, exe);
        }

        private void AddByPicker()
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() != true || string.IsNullOrEmpty(picker.SelectedProcessName)) return;

            var exe = picker.SelectedProcessName;
            var name = !string.IsNullOrWhiteSpace(picker.SelectedFriendlyName)
                ? picker.SelectedFriendlyName
                : Path.GetFileNameWithoutExtension(exe);
            AddProgram(name, exe);
        }

        private void AddProgram(string name, string exe)
        {
            if (_programs.Any(p => p.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"\"{exe}\" 已在列表中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var prog = new ProgramItem
            {
                Name = name,
                Exe = exe,
                Enabled = true,
                SaveIntervalSec = ConfigService.CheckIntervalSec
            };

            ProgramAdded?.Invoke(prog);
            RefreshList();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(ProgramListView.SelectedItem is ProgramDisplay display)) return;

            var result = MessageBox.Show(
                $"确定要删除 \"{display.Name}\" 吗？",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _programs.RemoveAll(p => p.Id == display.Id);
            _statuses.Remove(display.Id);
            _lastSaves.Remove(display.Name);
            ConfigService.SavePrograms(_programs);
            RefreshList();
            ProgramDeleted?.Invoke(display.Id);
        }

        private void OnToggleEnabledClick(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is ProgramDisplay display)) return;
            var prog = _programs.FirstOrDefault(p => p.Id == display.Id);
            if (prog == null) return;

            prog.Enabled = !prog.Enabled;
            ConfigService.SavePrograms(_programs);
            RefreshList();
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
                SettingsSaved?.Invoke();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
                app.OpenAboutDialog(this);
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
                return;
            }
            DragMove();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _countdownTimer?.Stop();
            Close();
        }

        private void ToggleMaximizeRestore()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
