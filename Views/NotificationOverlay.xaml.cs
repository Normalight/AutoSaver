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

        /// <summary>Raised after the toast has finished hiding.</summary>
        public event Action Hidden;

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
                    StatusBar.Background = ThemeBrush("SuccessColor", 0x34, 0xD3, 0x99);
                    TitleText.Text = $"✓ {programName}";
                    TitleText.Foreground = ThemeBrush("SuccessColor", 0x34, 0xD3, 0x99);
                    JumpButton.Visibility = Visibility.Collapsed;
                    CloseButton.Visibility = Visibility.Collapsed;
                    _autoHideTimer.Start();
                    break;

                case NotificationType.NeedsConfirm:
                    StatusBar.Background = ThemeBrush("WarningColor", 0xFB, 0xBF, 0x24);
                    TitleText.Text = $"⚠ {programName}";
                    TitleText.Foreground = ThemeBrush("WarningColor", 0xFB, 0xBF, 0x24);
                    DetailText.Text = detail;
                    JumpButton.Visibility = Visibility.Visible;
                    CloseButton.Visibility = Visibility.Visible;
                    break;

                case NotificationType.Failed:
                    StatusBar.Background = ThemeBrush("DangerColor", 0xF8, 0x71, 0x71);
                    TitleText.Text = $"✕ {programName}";
                    TitleText.Foreground = ThemeBrush("DangerColor", 0xF8, 0x71, 0x71);
                    DetailText.Text = detail;
                    JumpButton.Visibility = Visibility.Collapsed;
                    CloseButton.Visibility = Visibility.Visible;
                    break;
            }

            Show();
            Activate();
            SlideIn();
        }

        private Brush ThemeBrush(string resourceKey, byte r, byte g, byte b)
        {
            return TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void SlideIn()
        {
            // Use WPF SystemParameters (logical pixels) to avoid DPI mismatch with
            // Screen.WorkingArea which returns physical pixels.
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = -Height;

            var anim = new DoubleAnimation(workArea.Top + 10, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, e) => Top = workArea.Top + 10;
            BeginAnimation(TopProperty, anim);
        }

        private void HideAnimated()
        {
            var anim = new DoubleAnimation(-Height, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (s, e) =>
            {
                Hide();
                _onJump = null;
                Hidden?.Invoke();
            };
            BeginAnimation(TopProperty, anim);
        }

        private void OnJumpClick(object sender, RoutedEventArgs e)
        {
            _autoHideTimer.Stop();
            // Hide first so AutoSaver starts releasing the foreground, then invoke the
            // jump action. This gives SetForegroundWindow a better chance to succeed
            // because the target process can take the foreground while we're animating out.
            var jump = _onJump;
            HideAnimated();
            jump?.Invoke();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _autoHideTimer.Stop();
            HideAnimated();
        }
    }
}
