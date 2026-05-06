using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoSaver.Converters
{
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
                ? new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x98))   // muted when enabled (pause)
                : new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));   // accent when disabled (play)
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
                : new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
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
}
