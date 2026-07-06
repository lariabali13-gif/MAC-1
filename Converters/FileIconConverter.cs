using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MAC_1.Converters
{
    public class FileIconConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> _iconCache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string filename = value as string ?? "";
            if (string.IsNullOrWhiteSpace(filename))
                return GetDefaultIcon();

            string ext = Path.GetExtension(filename).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                return GetDefaultIcon();

            return _iconCache.GetOrAdd(ext, GetIconByExtension);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static BitmapSource GetIconByExtension(string ext)
        {
            try
            {
                // Create a temp file with the target extension to ask Windows for its icon
                string tempPath = Path.Combine(Path.GetTempPath(), "_mac1_icon" + ext);
                bool created = false;
                if (!File.Exists(tempPath))
                {
                    File.WriteAllText(tempPath, "");
                    created = true;
                }

                using var icon = Icon.ExtractAssociatedIcon(tempPath);
                if (created)
                {
                    try { File.Delete(tempPath); } catch { }
                }

                if (icon != null)
                {
                    return Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        new System.Windows.Int32Rect(0, 0, icon.Width, icon.Height),
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }

            return GetDefaultIcon();
        }

        private static BitmapSource GetDefaultIcon()
        {
            try
            {
                using var icon = SystemIcons.Application;
                return Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    new System.Windows.Int32Rect(0, 0, icon.Width, icon.Height),
                    BitmapSizeOptions.FromEmptyOptions());
            }
            catch
            {
                return BitmapSource.Create(1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
            }
        }
    }
}
