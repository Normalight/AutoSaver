using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        private class ProcessDisplay
        {
            public string DisplayName { get; set; }
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

                    list.Add(new ProcessDisplay
                    {
                        DisplayName = exeName,
                        ExeName = exeName,
                        Icon = GetProcessIcon(proc)
                    });
                }
                catch { }
            }

            _allProcesses = list;
            ApplyFilter("");
        }

        private static ImageSource GetProcessIcon(Process proc)
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return null;
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                return Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch { return null; }
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
                    .Where(p => p.ExeName.ToLowerInvariant().Contains(q))
                    .ToList();
            }
        }

        private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProcessList.SelectedItem is ProcessDisplay item)
            {
                SelectedProcessName = item.ExeName;
                DialogResult = true;
                Close();
            }
        }
    }
}
