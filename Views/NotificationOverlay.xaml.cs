using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AutoSaver.Views
{
    public enum NotificationType { Success, Failed }

    public partial class NotificationOverlay : Window
    {
        private const int MaxVisible = 3;
        private const int GWL_EX_STYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public event Action<string> PersistentClosed;

        public NotificationOverlay()
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
            PositionWindow();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(PositionWindow));
        }

        private void PositionWindow()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + 10;
        }

        public void Push(string programName, string detail, NotificationType type, bool isPersistent = false, string programId = null)
        {
            TrimExcess();

            var entry = CreateEntry(programName, detail, type, isPersistent, programId);
            NotificationStack.Children.Insert(0, entry);

            if (!IsVisible)
            {
                Show();
                PositionWindow();
            }

            AnimateSlideIn(entry);

            if (!isPersistent)
            {
                var autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                autoHide.Tick += (s, e) =>
                {
                    autoHide.Stop();
                    RemoveEntry(entry);
                };
                autoHide.Start();
            }
        }

        private void TrimExcess()
        {
            while (NotificationStack.Children.Count >= MaxVisible)
            {
                var last = NotificationStack.Children[NotificationStack.Children.Count - 1];
                AnimateSlideOut((FrameworkElement)last, remove: true);
            }
        }

        private Border CreateEntry(string programName, string detail, NotificationType type, bool isPersistent, string programId)
        {
            Brush accentBrush;
            string titleText;
            Brush titleBrush;

            if (type == NotificationType.Failed)
            {
                accentBrush = TryFindResource("DangerColor") as Brush ?? Brushes.Red;
                titleText = $"⚠ {programName}";
                titleBrush = TryFindResource("DangerColor") as Brush ?? Brushes.Red;
            }
            else
            {
                accentBrush = TryFindResource("SuccessColor") as Brush ?? Brushes.Green;
                titleText = $"✓ {programName}";
                titleBrush = TryFindResource("SuccessColor") as Brush ?? Brushes.Green;
            }

            var entry = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Background = TryFindResource("BgSecondary") as Brush,
                BorderBrush = TryFindResource("BorderColor") as Brush,
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.3
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var statusBar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(-14, 8, 10, 8),
                Background = accentBrush
            };
            Grid.SetColumn(statusBar, 0);
            grid.Children.Add(statusBar);

            var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
            var title = new TextBlock
            {
                Text = titleText,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = titleBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var detailBlock = new TextBlock
            {
                Text = detail,
                FontSize = 12,
                Foreground = TryFindResource("TextSecondary") as Brush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stackPanel.Children.Add(title);
            stackPanel.Children.Add(detailBlock);
            Grid.SetColumn(stackPanel, 1);
            grid.Children.Add(stackPanel);

            if (isPersistent)
            {
                var closeBtn = new Button
                {
                    Content = "✕",
                    Height = 28,
                    Width = 28,
                    FontSize = 14,
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                closeBtn.SetValue(FrameworkElement.StyleProperty, TryFindResource("GhostButton") as Style);
                closeBtn.Click += (s, e) =>
                {
                    if (programId != null)
                        PersistentClosed?.Invoke(programId);
                    RemoveEntry(entry);
                };
                Grid.SetColumn(closeBtn, 2);
                grid.Children.Add(closeBtn);
            }

            entry.Child = grid;
            return entry;
        }

        private void AnimateSlideIn(FrameworkElement entry)
        {
            entry.Opacity = 0;
            var transform = new TranslateTransform(0, -30);
            entry.RenderTransform = transform;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideDown = new DoubleAnimation(-30, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            entry.BeginAnimation(OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }

        private void RemoveEntry(FrameworkElement entry)
        {
            AnimateSlideOut(entry, remove: true);
        }

        private void AnimateSlideOut(FrameworkElement entry, bool remove)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var slideUp = new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) =>
            {
                if (remove)
                    NotificationStack.Children.Remove(entry);
                if (NotificationStack.Children.Count == 0)
                    Hide();
            };

            entry.RenderTransform = new TranslateTransform(0, 0);
            entry.BeginAnimation(OpacityProperty, fadeOut);
            ((TranslateTransform)entry.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            base.OnClosed(e);
        }
    }
}
