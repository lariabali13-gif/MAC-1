using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FontAwesome.WPF;
using MAC_1.Models;

namespace MAC_1.Views
{
    public partial class ContextPopup : UserControl
    {
        public event Action<string>? MenuItemClicked;
        private Popup? _ownerPopup;

        public ContextPopup()
        {
            InitializeComponent();
        }

        public void SetOwnerPopup(Popup popup)
        {
            _ownerPopup = popup;
        }

        public void ClosePopup()
        {
            if (_ownerPopup != null)
                _ownerPopup.IsOpen = false;
        }

        public void BuildForTask(DownloadTask task)
        {
            MenuItems.Children.Clear();

            bool isArchive = task.IsArchive;
            bool isCompleted = task.State == DownloadState.Completed;

            // Detect folder: extension is empty or path ends with separator
            string ext = System.IO.Path.GetExtension(task.Filename ?? "").ToLowerInvariant();
            bool isFolder = string.IsNullOrEmpty(ext);

            if (isCompleted)
            {
                AddItem("File Properties", FontAwesomeIcon.InfoCircle, "#6B7280");
                AddItem("Rename File", FontAwesomeIcon.Pencil, "#6B7280");
                AddItem("Copy File Path", FontAwesomeIcon.Copy, "#6B7280");
                AddItem("Copy Download Link", FontAwesomeIcon.Link, "#6B7280");
                AddSeparator();
                AddItem("Open With", FontAwesomeIcon.ExternalLink, "#6B7280");

                if (isArchive && !isFolder)
                {
                    // Archive files: only extract actions
                    AddSeparator();
                    AddItem("Extract Here", FontAwesomeIcon.FileZipOutline, "#8B5CF6");
                    AddItem("Extract To...", FontAwesomeIcon.FolderOpen, "#8B5CF6");
                }

                if (isFolder)
                {
                    // Folders: only compress actions
                    AddSeparator();
                    AddItem("Compress to ZIP", FontAwesomeIcon.Compress, "#6B7280");
                    AddItem("Compress to RAR", FontAwesomeIcon.Compress, "#6B7280");
                }

                AddSeparator();
                AddPlaceholder("Share", FontAwesomeIcon.ShareAlt, "#9CA3AF");
            }
            else
            {
                AddItem("Copy Download Link", FontAwesomeIcon.Link, "#6B7280");
                AddItem("Download Properties", FontAwesomeIcon.InfoCircle, "#6B7280");
                AddItem("Change Save Location", FontAwesomeIcon.FolderOpen, "#6B7280");
                AddSeparator();
                AddPlaceholder("Schedule Download", FontAwesomeIcon.Hourglass, "#9CA3AF");
                AddSeparator();
                AddPlaceholder("Download Details", FontAwesomeIcon.ListUl, "#9CA3AF");
                AddPlaceholder("Properties", FontAwesomeIcon.Cog, "#9CA3AF");

                if (isArchive && !isFolder)
                {
                    // Archive files: only extract actions
                    AddSeparator();
                    AddItem("Extract Here", FontAwesomeIcon.FileZipOutline, "#8B5CF6");
                    AddItem("Extract To...", FontAwesomeIcon.FolderOpen, "#8B5CF6");
                }

                if (isFolder)
                {
                    // Folders: only compress actions
                    AddSeparator();
                    AddItem("Compress to ZIP", FontAwesomeIcon.Compress, "#6B7280");
                    AddItem("Compress to RAR", FontAwesomeIcon.Compress, "#6B7280");
                }
            }
        }

        private void AddItem(string text, FontAwesomeIcon icon, string colorHex)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("MenuItemBtn"),
                Tag = text
            };
            btn.Click += (_, _) =>
            {
                MenuItemClicked?.Invoke(text);
                ClosePopup();
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new ImageAwesome
            {
                Icon = icon,
                Width = 14, Height = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151"))
            });

            btn.Content = sp;
            MenuItems.Children.Add(btn);
        }

        private void AddPlaceholder(string text, FontAwesomeIcon icon, string colorHex)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("PlaceholderBtn"),
                Tag = text
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new ImageAwesome
            {
                Icon = icon,
                Width = 14, Height = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9CA3AF"))
            });

            btn.Content = sp;
            MenuItems.Children.Add(btn);
        }

        private void AddSeparator()
        {
            MenuItems.Children.Add(new Border
            {
                Height = 1,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E5E7EB")),
                Margin = new Thickness(8, 4, 8, 4)
            });
        }
    }
}
