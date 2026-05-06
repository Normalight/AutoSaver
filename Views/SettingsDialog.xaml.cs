using System;
using System.Windows;
using AutoSaver.Services;

namespace AutoSaver.Views
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();

            // Load current settings
            var theme = ThemeService.CurrentTheme;
            ThemeCombo.SelectedIndex = (int)theme;

            IntervalBox.Text = ConfigService.CheckIntervalSec.ToString();
            StartupCheck.IsChecked = ConfigService.StartWithWindows;
            TrayCloseCheck.IsChecked = ConfigService.MinimizeToTrayOnClose;
            NotifyCheck.IsChecked = ConfigService.ShowNotifications;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Validate
            if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 1)
            {
                MessageBox.Show("检查间隔必须为大于 0 的整数。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save
            ThemeService.CurrentTheme = (AppTheme)ThemeCombo.SelectedIndex;
            ConfigService.CheckIntervalSec = interval;
            ConfigService.StartWithWindows = StartupCheck.IsChecked == true;
            ConfigService.MinimizeToTrayOnClose = TrayCloseCheck.IsChecked == true;
            ConfigService.ShowNotifications = NotifyCheck.IsChecked == true;

            // Apply theme immediately
            ThemeService.ApplyTheme(Application.Current);

            DialogResult = true;
            Close();
        }
    }
}
