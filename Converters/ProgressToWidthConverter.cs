using System;
using System.Globalization;
using System.Windows.Data;

namespace MAC_1.Converters
{
    public class ProgressToWidthConverter : IValueConverter
    {
        public static readonly ProgressToWidthConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double progress)
            {
                // Max width is 200 pixels for the progress bar
                return Math.Max(0, Math.Min(200, progress * 2));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
