using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoSaver.Services;

namespace AutoSaver.Views
{
    public partial class ProcessPickerDialog : Window
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private static readonly HashSet<string> Blacklist = new HashSet<string>(
            new[] { "system", "system idle process", "svchost", "csrss", "smss",
                    "wininit", "services", "lsass", "winlogon", "taskmgr", "autosaver" },
            StringComparer.OrdinalIgnoreCase);

        private List<ProcessDisplay> _allProcesses;

        public string SelectedProcessName { get; private set; }
        public string SelectedFriendlyName { get; private set; }

        private class ProcessDisplay
        {
            public string DisplayName { get; set; }   // friendly name (FileDescription / ProductName)
            public string ExeName { get; set; }
            public ImageSource Icon { get; set; }
        }

        public ProcessPickerDialog()
        {
            InitializeComponent();

            SearchBox.Text = "搜索应用名...";
            var mutedBrush = TryFindResource("TextMuted") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;
            var primaryBrush = TryFindResource("TextPrimary") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Black;

            SearchBox.Foreground = mutedBrush;
            SearchBox.GotFocus += (s, e) =>
            {
                if (SearchBox.Text == "搜索应用名...")
                {
                    SearchBox.Text = "";
                    SearchBox.Foreground = primaryBrush;
                }
            };
            SearchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    SearchBox.Text = "搜索应用名...";
                    SearchBox.Foreground = mutedBrush;
                }
            };

            LoadProcesses();
        }

        private void LoadProcesses()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<ProcessDisplay>();

            foreach (var proc in Process.GetProcesses().OrderBy(p => p.ProcessName))
            {
                try
                {
                    if (Blacklist.Contains(proc.ProcessName)) continue;
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    if (!IsWindowVisible(proc.MainWindowHandle)) continue;
                    if (GetWindowTextLength(proc.MainWindowHandle) == 0) continue;

                    var exeName = proc.ProcessName + ".exe";
                    if (!seen.Add(exeName)) continue;

                    var path = ExecutableMetadataService.GetProcessPath(proc);
                    var friendlyName = ExecutableMetadataService.GetFriendlyName(path) ?? proc.ProcessName;

                    list.Add(new ProcessDisplay
                    {
                        DisplayName = friendlyName,
                        ExeName = exeName,
                        Icon = ExecutableMetadataService.GetIcon(path)
                    });
                }
                catch { }
            }

            _allProcesses = list;
            ApplyFilter("");
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text;
            if (query == "搜索应用名...") query = "";
            ApplyFilter(query);
        }

        private void ApplyFilter(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                ProcessList.ItemsSource = _allProcesses;
            else
            {
                var q = query.ToLowerInvariant();
                ProcessList.ItemsSource = _allProcesses
                    .Where(p => p.DisplayName.ToLowerInvariant().Contains(q)
                             || p.ExeName.ToLowerInvariant().Contains(q))
                    .ToList();
            }
        }

        private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void OnListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                ConfirmSelection();
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }

        private void ConfirmSelection()
        {
            if (ProcessList.SelectedItem is ProcessDisplay item)
            {
                SelectedProcessName = item.ExeName;
                SelectedFriendlyName = item.DisplayName;
                DialogResult = true;
                Close();
            }
        }
    }
}
