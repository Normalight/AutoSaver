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

namespace AutoSaver.Views
{
    public partial class MainWindow : Window
    {
        private List<ProgramItem> _programs;
        private readonly Dictionary<string, string> _statuses = new Dictionary<string, string>();
        private readonly Dictionary<string, Tuple<string, int>> _lastSaves = new Dictionary<string, Tuple<string, int>>();
        private List<WindowCountdownRow> _lastWindowRows = new List<WindowCountdownRow>();
        /// <summary>多窗口分组 Expander 是否展开（按 ProgramId 记忆）。</summary>
        private readonly Dictionary<string, bool> _programGroupExpanded =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly SolidColorBrush FallbackSuccessBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        private static readonly SolidColorBrush FallbackMutedBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x98));

        public event Action<ProgramItem> ProgramAdded;
        public event Action<string> ProgramDeleted;
        /// <summary>Fired when Settings dialog saves successfully (interval/theme/etc.).</summary>
        public event Action SettingsSaved;

        /// <summary>Updates per-window countdown rows for the program list.</summary>
        public void ApplyProgramCountdowns(IReadOnlyList<WindowCountdownRow> rows)
        {
            _lastWindowRows = rows?.ToList() ?? new List<WindowCountdownRow>();
            RefreshList();
        }

        /// <summary>Shows countdown for the monitored program that currently has keyboard foreground.</summary>
        public void UpdateFocusCountdown(FocusCountdownSnapshot snap)
        {
            if (!snap.ShowCapsule)
            {
                CountdownCapsule.Visibility = Visibility.Collapsed;
                return;
            }

            CountdownCapsule.Visibility = Visibility.Visible;
            var sec = Math.Max(0, snap.RemainingSec);
            var timeStr = sec >= 60
                ? $"{sec / 60}m {sec % 60:D2}s"
                : $"{sec}s";

            CountdownLabel.Text = string.IsNullOrWhiteSpace(snap.ProgramDisplayName)
                ? timeStr
                : $"{snap.ProgramDisplayName} · {timeStr}";
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
            var selectedProgramId = (ProgramListView.SelectedItem as ProgramDisplay)?.ProgramId;

            var byProg = _lastWindowRows
                .GroupBy(r => r.ProgramId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.WindowTitle, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.Ordinal);

            var displayItems = new List<ProgramDisplay>();
            foreach (var p in _programs)
            {
                var isRunning = _statuses.TryGetValue(p.Id, out var running) && running == "running";
                var exePath = GetExePath(p.Exe);
                var statusBrush = isRunning
                    ? (TryFindResource("SuccessColor") as Brush ?? FallbackSuccessBrush)
                    : (TryFindResource("TextMuted") as Brush ?? FallbackMutedBrush);
                var icon = GetIconFromPath(exePath);
                var displayName = CreateDisplayName(p.Name, p.Exe, exePath);
                var exeFileSummary = CreateExeSummary(p.Exe);

                bool expanded = _programGroupExpanded.TryGetValue(p.Id, out var expVal) ? expVal : true;

                ProgramDisplay Row(bool multi, List<WindowSubRow> subs, string timerLine, string exeSummaryLine)
                {
                    return new ProgramDisplay
                    {
                        RowId = p.Id,
                        ProgramId = p.Id,
                        HasMultipleWindows = multi,
                        SubWindows = subs,
                        IsExpanded = expanded,
                        Name = displayName,
                        Exe = p.Exe,
                        ExeSummary = exeSummaryLine,
                        StatusColor = statusBrush,
                        Enabled = p.Enabled,
                        Icon = icon,
                        TimerStatus = timerLine
                    };
                }

                if (!p.Enabled)
                {
                    displayItems.Add(Row(false, null, "倒计时 · 已禁用", exeFileSummary));
                    continue;
                }

                if (!isRunning)
                {
                    displayItems.Add(Row(false, null, "倒计时 · 未运行", exeFileSummary));
                    continue;
                }

                if (!byProg.TryGetValue(p.Id, out var wins) || wins.Count == 0)
                {
                    var interval = p.SaveIntervalSec > 0 ? p.SaveIntervalSec : ConfigService.CheckIntervalSec;
                    displayItems.Add(Row(false, null,
                        $"倒计时 · 暂无可见窗口 · 周期 {FormatTickSec(interval)}", exeFileSummary));
                    continue;
                }

                if (wins.Count == 1)
                {
                    var w = wins[0];
                    var title = string.IsNullOrWhiteSpace(w.WindowTitle) ? "（无标题）" : w.WindowTitle;
                    displayItems.Add(Row(false, null, FormatWindowTimerStatus(w), title));
                    continue;
                }

                var subRows = wins.Select(w => new WindowSubRow
                {
                    WindowTitle = string.IsNullOrWhiteSpace(w.WindowTitle) ? "（无标题）" : w.WindowTitle,
                    TimerStatus = FormatWindowTimerStatus(w)
                }).ToList();

                displayItems.Add(Row(true, subRows,
                    $"共 {wins.Count} 个窗口 · 展开查看各窗口倒计时",
                    exeFileSummary));
            }

            ProgramListView.ItemsSource = displayItems;

            if (selectedProgramId != null)
            {
                var item = displayItems.FirstOrDefault(d => d.ProgramId == selectedProgramId);
                if (item != null) ProgramListView.SelectedItem = item;
            }

            UpdateDeleteButton();
        }

        private void OnProgramGroupExpandedChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is Expander ex)) return;
            var id = ex.Tag as string;
            if (string.IsNullOrEmpty(id)) return;
            _programGroupExpanded[id] = ex.IsExpanded;
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

        private static string FormatWindowTimerStatus(WindowCountdownRow w)
        {
            if (!w.Active)
                return "倒计时 · —";
            if (w.RemainingSec <= 0)
                return "倒计时 · 已到期 · 切回此窗口保存";
            return $"倒计时 · 剩余 {FormatTickSec(w.RemainingSec)} / 周期 {FormatTickSec(w.IntervalSec)}";
        }

        private static string FormatTickSec(int sec)
        {
            sec = Math.Max(0, sec);
            if (sec >= 3600)
                return $"{sec / 3600} 小时 {sec % 3600 / 60} 分";
            if (sec >= 60)
                return $"{sec / 60} 分 {sec % 60} 秒";
            return $"{sec} 秒";
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

            var prog = _programs.FirstOrDefault(x => x.Id == display.ProgramId);
            _programs.RemoveAll(x => x.Id == display.ProgramId);
            _statuses.Remove(display.ProgramId);
            _programGroupExpanded.Remove(display.ProgramId);
            if (prog != null)
                _lastSaves.Remove(prog.Name);
            ConfigService.SavePrograms(_programs);
            RefreshList();
            ProgramDeleted?.Invoke(display.ProgramId);
        }

        private void OnToggleEnabledClick(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is ProgramDisplay display)) return;
            var prog = _programs.FirstOrDefault(x => x.Id == display.ProgramId);
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
            Close();
        }

        private void ToggleMaximizeRestore()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
