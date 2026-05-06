using System;
using System.Windows;
using AutoSaver.Models;
using AutoSaver.Services;
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
                ExeBox.Text = System.IO.Path.GetFileName(dlg.FileName);
                var exeLocal = ExeBox.Text;
                var stem = ProgramItem.GetExeStemDisplay(exeLocal);
                NameBox.Text = string.IsNullOrEmpty(stem)
                    ? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName)
                    : stem;
            }
        }

        private void OnPickRunningClick(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog();
            picker.Owner = this;
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedProcessName))
            {
                ExeBox.Text = picker.SelectedProcessName;
                var stem = ProgramItem.GetExeStemDisplay(picker.SelectedProcessName);
                NameBox.Text = string.IsNullOrEmpty(stem)
                    ? System.IO.Path.GetFileNameWithoutExtension(picker.SelectedProcessName)
                    : stem;
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

            if (string.IsNullOrEmpty(ProgramItem.NormalizeExeKey(exe)))
            {
                MessageBox.Show("无法识别该可执行文件名。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = _existing ?? new ProgramItem();
            var stemDefault = ProgramItem.GetExeStemDisplay(exe);
            Result.Name = name.Length > 0
                ? name
                : (!string.IsNullOrEmpty(stemDefault)
                    ? stemDefault
                    : System.IO.Path.GetFileNameWithoutExtension(exe));
            Result.Exe = exe;
            Result.SaveIntervalSec = ConfigService.CheckIntervalSec;
            Result.Enabled = EnabledCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}
