using System;
using System.Windows;
using System.Windows.Input;
using AutoSaver.Services;

namespace AutoSaver.Views
{
    public partial class SettingsDialog : Window
    {
        private AppTheme _selectedTheme;

        public SettingsDialog()
        {
            InitializeComponent();

            _selectedTheme = ThemeService.CurrentTheme;
            UpdateThemeButtons();

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
            UpdateCheckOnStartupCheck.IsChecked = ConfigService.CheckUpdatesOnStartup;
        }

        private void UpdateThemeButtons()
        {
            var accent = TryFindResource("AccentColor") as System.Windows.Media.Brush;
            var normal = TryFindResource("ControlBackground") as System.Windows.Media.Brush;
            var accentFg = System.Windows.Media.Brushes.White;
            var normalFg = TryFindResource("TextPrimary") as System.Windows.Media.Brush;

            ThemeDarkBtn.Background   = _selectedTheme == AppTheme.Dark   ? accent : normal;
            ThemeDarkBtn.Foreground   = _selectedTheme == AppTheme.Dark   ? accentFg : normalFg;
            ThemeLightBtn.Background  = _selectedTheme == AppTheme.Light  ? accent : normal;
            ThemeLightBtn.Foreground  = _selectedTheme == AppTheme.Light  ? accentFg : normalFg;
            ThemeSystemBtn.Background = _selectedTheme == AppTheme.System ? accent : normal;
            ThemeSystemBtn.Foreground = _selectedTheme == AppTheme.System ? accentFg : normalFg;
        }

        private void OnThemeDarkClick(object sender, RoutedEventArgs e)
        {
            _selectedTheme = AppTheme.Dark;
            UpdateThemeButtons();
        }

        private void OnThemeLightClick(object sender, RoutedEventArgs e)
        {
            _selectedTheme = AppTheme.Light;
            UpdateThemeButtons();
        }

        private void OnThemeSystemClick(object sender, RoutedEventArgs e)
        {
            _selectedTheme = AppTheme.System;
            UpdateThemeButtons();
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

            ThemeService.CurrentTheme = _selectedTheme;
            ConfigService.CheckIntervalSec = intervalSec;
            ConfigService.StartWithWindows = StartupCheck.IsChecked == true;
            ConfigService.MinimizeToTrayOnClose = TrayCloseCheck.IsChecked == true;
            ConfigService.ShowNotifications = NotifyCheck.IsChecked == true;
            ConfigService.CheckUpdatesOnStartup = UpdateCheckOnStartupCheck.IsChecked == true;

            ThemeService.ApplyTheme(Application.Current);

            DialogResult = true;
            Close();
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2) return;
            DragMove();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
