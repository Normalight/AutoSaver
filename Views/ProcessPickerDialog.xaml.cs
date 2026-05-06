using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

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
                var path = GetProcessPath(proc);
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

        private static string GetProcessPath(Process proc)
        {
            try
            {
                // Try MainModule first (fast, but can throw for elevated/system processes)
                return proc.MainModule?.FileName;
            }
            catch
            {
                // Fallback via P/Invoke
                try
                {
                    var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                    if (hProcess == IntPtr.Zero) return null;
                    var sb = new StringBuilder(1024);
                    var size = sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                        return sb.ToString();
                    CloseHandle(hProcess);
                }
                catch { }
                return null;
            }
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
                DialogResult = true;
                Close();
            }
        }
    }
}
