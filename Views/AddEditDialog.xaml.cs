using System;
using System.Windows;
using AutoSaver.Models;
using Microsoft.Win32;

namespace AutoSaver.Views
{
    public partial class AddEditDialog : Window
    {
        private readonly ProgramItem _existing;

        public ProgramItem Result { get; private set; }

        public AddEditDialog(ProgramItem program = null)
        {
            InitializeComponent();
            _existing = program;

            if (program != null)
            {
                Title = "编辑程序";
                NameBox.Text = program.Name;
                ExeBox.Text = program.Exe;
                IntervalBox.Text = (program.SaveIntervalSec / 60).ToString();
                EnabledCheck.IsChecked = program.Enabled;
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe",
                Title = "选择目标程序"
            };

            try { dlg.InitialDirectory = @"C:\Program Files"; }
            catch { dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); }

            if (dlg.ShowDialog() == true)
            {
                var exeName = System.IO.Path.GetFileName(dlg.FileName);
                ExeBox.Text = exeName;
                NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void OnPickRunningClick(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog();
            picker.Owner = this;
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedProcessName))
            {
                ExeBox.Text = picker.SelectedProcessName;
                NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(picker.SelectedProcessName);
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            var exe = ExeBox.Text.Trim();
            if (string.IsNullOrEmpty(exe))
            {
                MessageBox.Show("请输入进程名。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(IntervalBox.Text, out var minutes) || minutes < 1)
            {
                MessageBox.Show("保存间隔必须为大于 0 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = _existing ?? new ProgramItem();
            Result.Name = name.Length > 0 ? name : System.IO.Path.GetFileNameWithoutExtension(exe);
            Result.Exe = exe;
            Result.SaveIntervalSec = minutes * 60;
            Result.Enabled = EnabledCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}
