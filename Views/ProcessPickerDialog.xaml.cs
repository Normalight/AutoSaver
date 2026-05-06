using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AutoSaver.Views
{
    public partial class ProcessPickerDialog : Window
    {
        private static readonly HashSet<string> Blacklist = new HashSet<string>(
            new[] { "system", "system idle process", "svchost", "csrss", "smss",
                    "wininit", "services", "lsass", "winlogon", "explorer",
                    "taskmgr", "autosaver" },
            StringComparer.OrdinalIgnoreCase);

        private List<ProcessDisplay> _allProcesses;

        public string SelectedProcessName { get; private set; }

        private class ProcessDisplay
        {
            public string DisplayName { get; set; }
            public string ExeName { get; set; }
        }

        public ProcessPickerDialog()
        {
            InitializeComponent();

            // Set placeholder text with theme-aware colors
            SearchBox.Text = "搜索进程名...";
            var mutedBrush = TryFindResource("TextMuted") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;
            var primaryBrush = TryFindResource("TextPrimary") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Black;

            SearchBox.Foreground = mutedBrush;
            SearchBox.GotFocus += (s, e) =>
            {
                if (SearchBox.Text == "搜索进程名...")
                {
                    SearchBox.Text = "";
                    SearchBox.Foreground = primaryBrush;
                }
            };
            SearchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    SearchBox.Text = "搜索进程名...";
                    SearchBox.Foreground = mutedBrush;
                }
            };

            LoadProcesses();
        }

        private void LoadProcesses()
        {
            var exes = new SortedSet<string>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (!string.IsNullOrEmpty(name) && !Blacklist.Contains(name))
                        exes.Add(name + ".exe");
                }
                catch { }
            }

            _allProcesses = exes.Select(e => new ProcessDisplay
            {
                DisplayName = e,
                ExeName = e
            }).ToList();

            ApplyFilter("");
            ProcessList.DisplayMemberPath = "DisplayName";
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text;
            if (query == "搜索进程名...") query = "";
            ApplyFilter(query);
        }

        private void ApplyFilter(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ProcessList.ItemsSource = _allProcesses;
            }
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
