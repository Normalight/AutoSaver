using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AutoSaver.Views
{
    public enum NotificationType { Success, NeedsConfirm, Failed }

    public partial class NotificationOverlay : Window
    {
        private readonly DispatcherTimer _autoHideTimer;
        private Action _onJump;

        public NotificationOverlay()
        {
            InitializeComponent();
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _autoHideTimer.Tick += (s, e) => { _autoHideTimer.Stop(); HideAnimated(); };
        }

        public void Show(string programName, string detail, NotificationType type, Action onJump = null)
        {
            _onJump = onJump;
            _autoHideTimer.Stop();

            // Set content
            TitleText.Text = $"{programName}";
            DetailText.Text = detail;

            switch (type)
            {
                case NotificationType.Success:
                    StatusBar.Background = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                    TitleText.Text = $"✓ {programName} 已保存";
                    TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                    JumpButton.Visibility = Visibility.Collapsed;
                    CloseButton.Visibility = Visibility.Collapsed;
                    _autoHideTimer.Start();
                    break;

                case NotificationType.NeedsConfirm:
                    StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
                    TitleText.Text = $"⚠ {programName}";
                    TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
                    DetailText.Text = detail;
                    JumpButton.Visibility = Visibility.Visible;
                    CloseButton.Visibility = Visibility.Visible;
                    break;

                case NotificationType.Failed:
                    StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                    TitleText.Text = $"✕ {programName}";
                    TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                    DetailText.Text = detail;
                    JumpButton.Visibility = Visibility.Collapsed;
                    CloseButton.Visibility = Visibility.Visible;
                    break;
            }

            Show();
            Activate();
            SlideIn();
        }

        private void SlideIn()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            var workingArea = screen.WorkingArea;
            Left = (workingArea.Width - Width) / 2 + workingArea.Left;
            Top = -Height;

            var anim = new DoubleAnimation(workingArea.Top + 10, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, e) => Top = workingArea.Top + 10;
            BeginAnimation(TopProperty, anim);
        }

        private void HideAnimated()
        {
            var anim = new DoubleAnimation(-Height, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (s, e) => { Hide(); _onJump = null; };
            BeginAnimation(TopProperty, anim);
        }

        private void OnJumpClick(object sender, RoutedEventArgs e)
        {
            _autoHideTimer.Stop();
            _onJump?.Invoke();
            HideAnimated();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _autoHideTimer.Stop();
            HideAnimated();
        }
    }
}
