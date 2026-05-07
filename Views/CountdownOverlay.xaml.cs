using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoSaver.Services;

namespace AutoSaver.Views
{
    public partial class CountdownOverlay : Window
    {
        private DispatcherTimer _animationTimer;

        private const int GWL_EX_STYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public CountdownOverlay()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EX_STYLE);
            SetWindowLong(hwnd, GWL_EX_STYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            LoadPosition();
        }

        public void UpdateCountdown(int remainingSec, int intervalSec)
        {
            if (_animationTimer != null && _animationTimer.IsEnabled)
                return;

            var sec = Math.Max(0, remainingSec);
            CountdownText.Text = sec >= 60
                ? $"{sec / 60}m {sec % 60:D2}s"
                : $"{sec}s";
        }

        public void PlaySaveAnimation(bool success)
        {
            if (!ConfigService.ShowSaveAnimation) return;

            CountdownText.Text = success ? "✓" : "✕";
            CountdownText.Foreground = TryFindResource(success ? "CountdownOverlaySuccessText" : "CountdownOverlayFailText") as Brush;
            CapsuleBorder.Background = TryFindResource(success ? "CountdownOverlaySuccessBg" : "CountdownOverlayFailBg") as Brush;
            CapsuleBorder.BorderBrush = TryFindResource(success ? "CountdownOverlaySuccessBorder" : "CountdownOverlayFailBorder") as Brush;

            if (success)
            {
                CapsuleBorder.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                var transform = new ScaleTransform(1, 1);
                CapsuleBorder.RenderTransform = transform;
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
            }
            else
            {
                var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
                shake.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                shake.KeyFrames.Add(new DiscreteDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
                shake.KeyFrames.Add(new DiscreteDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
                shake.KeyFrames.Add(new DiscreteDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
                shake.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
                var transform = new TranslateTransform(0, 0);
                CapsuleBorder.RenderTransform = transform;
                transform.BeginAnimation(TranslateTransform.XProperty, shake);
            }

            _animationTimer?.Stop();
            _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _animationTimer.Tick += (s, e) =>
            {
                _animationTimer.Stop();
                ResetVisuals();
            };
            _animationTimer.Start();
        }

        private void ResetVisuals()
        {
            CountdownText.Foreground = TryFindResource("CountdownOverlayText") as Brush;
            CapsuleBorder.Background = TryFindResource("CountdownOverlayBg") as Brush;
            CapsuleBorder.BorderBrush = TryFindResource("CountdownOverlayBorder") as Brush;
            CapsuleBorder.RenderTransform = null;
        }

        public void ShowAnimated()
        {
            Show();
            Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        public void HideAnimated()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) => Hide();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        public void ValidatePosition()
        {
            if (!IsPositionOnScreen())
                SetDefaultPosition();
        }

        private void LoadPosition()
        {
            var x = ConfigService.CountdownX;
            var y = ConfigService.CountdownY;

            if (!double.IsNaN(x) && !double.IsNaN(y))
            {
                Left = x;
                Top = y;
                if (!IsPositionOnScreen())
                    SetDefaultPosition();
            }
            else
            {
                SetDefaultPosition();
            }
        }

        private void SetDefaultPosition()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
        }

        private bool IsPositionOnScreen()
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var area = screen.WorkingArea;
                var source = PresentationSource.FromVisual(this);
                double dpiX = 1.0, dpiY = 1.0;
                if (source != null)
                {
                    dpiX = source.CompositionTarget.TransformFromDevice.M11;
                    dpiY = source.CompositionTarget.TransformFromDevice.M22;
                }

                var winLeft = Left;
                var winTop = Top;
                var winRight = Left + Width;
                var winBottom = Top + Height;

                var screenLeft = area.Left * dpiX;
                var screenTop = area.Top * dpiY;
                var screenRight = area.Right * dpiX;
                var screenBottom = area.Bottom * dpiY;

                if (winLeft >= screenLeft + 8 && winRight <= screenRight - 8 &&
                    winTop >= screenTop + 8 && winBottom <= screenBottom - 8)
                    return true;
            }
            return false;
        }

        private void SavePosition()
        {
            ConfigService.CountdownX = Left;
            ConfigService.CountdownY = Top;
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            DragMove();
            SavePosition();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && !IsPositionOnScreen())
                    SetDefaultPosition();
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            base.OnClosed(e);
        }
    }
}
