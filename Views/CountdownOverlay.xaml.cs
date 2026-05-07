using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

            var flashBrush = TryFindResource(success ? "CountdownOverlaySuccessBg" : "CountdownOverlayFailBg") as Brush;
            FlashLayer.Background = flashBrush;
            FlashLayer.BeginAnimation(OpacityProperty, null);

            var opacityAnimation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(760) };
            opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(success ? 0.42 : 0.38, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            FlashLayer.BeginAnimation(OpacityProperty, opacityAnimation);

            ContentPanel.RenderTransformOrigin = new Point(0.5, 0.5);
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(1, 1);
            var translateTransform = new TranslateTransform(0, 0);
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);
            ContentPanel.RenderTransform = transformGroup;

            if (success)
            {
                var scale = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
                    { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } });
                scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

                var lift = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(-1.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                translateTransform.BeginAnimation(TranslateTransform.YProperty, lift);
            }
            else
            {
                var drift = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
                drift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                drift.KeyFrames.Add(new EasingDoubleKeyFrame(1.6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                drift.KeyFrames.Add(new EasingDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                drift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                translateTransform.BeginAnimation(TranslateTransform.XProperty, drift);

                var settle = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
                settle.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                settle.KeyFrames.Add(new EasingDoubleKeyFrame(0.96, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                settle.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
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
            FlashLayer.Background = Brushes.Transparent;
            FlashLayer.BeginAnimation(OpacityProperty, null);
            FlashLayer.Opacity = 0;
            ContentPanel.RenderTransform = null;
        }

        public void ShowAnimated()
        {
            Show();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 0;

                RootChrome.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = RootChrome.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
                if (!(RootChrome.RenderTransform is ScaleTransform))
                    RootChrome.RenderTransform = scale;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 0.88;
                scale.ScaleY = 0.88;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeIn);

                var scaleOut = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
                scaleOut.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scaleOut.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                scaleOut.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
            }), DispatcherPriority.Render);
        }

        public void HideAnimated()
        {
            RootChrome.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = RootChrome.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            if (!(RootChrome.RenderTransform is ScaleTransform))
                RootChrome.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) => Hide();

            var scaleIn = new DoubleAnimation(1.0, 0.94, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            BeginAnimation(OpacityProperty, fadeOut);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
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
