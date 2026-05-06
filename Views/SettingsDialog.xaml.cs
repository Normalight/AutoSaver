using System;
using System.Windows;
using System.Windows.Input;
using AutoSaver.Services;

namespace AutoSaver.Views
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();

            var theme = ThemeService.CurrentTheme;
            ThemeCombo.SelectedIndex = (int)theme;

            var intervalSec = ConfigService.CheckIntervalSec;
            if (intervalSec % 3600 == 0)
            {
                IntervalValueBox.Text = (intervalSec / 3600).ToString();
                IntervalUnitCombo.SelectedIndex = 2;
            }
            else if (intervalSec % 60 == 0)
            {
                IntervalValueBox.Text = (intervalSec / 60).ToString();
                IntervalUnitCombo.SelectedIndex = 1;
            }
            else
            {
                IntervalValueBox.Text = intervalSec.ToString();
                IntervalUnitCombo.SelectedIndex = 0;
            }
            StartupCheck.IsChecked = ConfigService.StartWithWindows;
            TrayCloseCheck.IsChecked = ConfigService.MinimizeToTrayOnClose;
            NotifyCheck.IsChecked = ConfigService.ShowNotifications;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(IntervalValueBox.Text, out var interval) || interval < 1)
            {
                MessageBox.Show("检查间隔必须为大于 0 的整数。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var unitSeconds = IntervalUnitCombo.SelectedIndex switch
            {
                1 => 60,
                2 => 3600,
                _ => 1
            };

            int intervalSec;
            try
            {
                intervalSec = checked(interval * unitSeconds);
            }
            catch (OverflowException)
            {
                MessageBox.Show("检查间隔数值过大。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ThemeService.CurrentTheme = (AppTheme)ThemeCombo.SelectedIndex;
            ConfigService.CheckIntervalSec = intervalSec;
            ConfigService.StartWithWindows = StartupCheck.IsChecked == true;
            ConfigService.MinimizeToTrayOnClose = TrayCloseCheck.IsChecked == true;
            ConfigService.ShowNotifications = NotifyCheck.IsChecked == true;

            ThemeService.ApplyTheme(Application.Current);

            DialogResult = true;
            Close();
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
                return;

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
    }
}
