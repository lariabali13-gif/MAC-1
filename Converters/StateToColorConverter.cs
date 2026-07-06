using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MAC_1.Models;

namespace MAC_1.Converters
{
    public class StateToColorConverter : IValueConverter
    {
        public static readonly StateToColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadState state)
            {
                return state switch
                {
                    DownloadState.Downloading => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    DownloadState.Paused => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                    DownloadState.Completed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                    DownloadState.Failed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                    DownloadState.Queued => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")),
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"))
                };
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
