using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoSaver.Converters
{
    internal static class ThemeBrushHelper
    {
        public static SolidColorBrush TryBrush(string resourceKey, byte r, byte g, byte b)
        {
            if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush br)
                return br;
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
    }

    // "暂停" path when enabled, "播放" path when disabled
    public class BoolToPausePlayPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool enabled = value is bool b && b;
            // enabled → pause icon (two vertical bars)
            // disabled → play icon (triangle)
            return enabled
                ? "M6 4h4v16H6zM14 4h4v16h-4z"
                : "M5 3l14 9-14 9V3z";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToToggleColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool enabled = value is bool b && b;
            return enabled
                ? ThemeBrushHelper.TryBrush("TextMuted", 0x8E, 0x8E, 0x98)
                : ThemeBrushHelper.TryBrush("AccentColor", 0x63, 0x66, 0xF1);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToToggleFillConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool enabled = value is bool b && b;
            // play icon needs fill, pause icon uses stroke only
            return enabled
                ? Brushes.Transparent
                : ThemeBrushHelper.TryBrush("AccentColor", 0x63, 0x66, 0xF1);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToToggleTipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool enabled = value is bool b && b;
            return enabled ? "暂停监控" : "启用监控";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>用于「非多窗口」分支：HasMultipleWindows 为 false 时显示。</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
