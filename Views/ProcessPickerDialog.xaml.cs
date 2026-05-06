using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace AutoSaver.Views
{
    public partial class ProcessPickerDialog : Window
    {
        private static readonly HashSet<string> Blacklist = new HashSet<string>(
            new[] { "system", "system idle process", "svchost", "csrss", "smss",
                    "wininit", "services", "lsass", "winlogon", "explorer",
                    "taskmgr", "autosaver" },
            StringComparer.OrdinalIgnoreCase);

        public string SelectedProcessName { get; private set; }

        public ProcessPickerDialog()
        {
            InitializeComponent();
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

            ProcessList.ItemsSource = exes.Select(e => new { DisplayName = e }).ToList();
        }

        private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProcessList.SelectedItem != null)
            {
                dynamic item = ProcessList.SelectedItem;
                SelectedProcessName = item.DisplayName;
                DialogResult = true;
                Close();
            }
        }
    }
}
