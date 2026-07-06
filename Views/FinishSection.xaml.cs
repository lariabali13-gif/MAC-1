using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MAC_1.Models;
using MAC_1.Services;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class FinishSection : UserControl
    {
        public FinishSection()
        {
            InitializeComponent();
            DataContext = FinishViewModel.Instance;
            this.Loaded += (_, _) => UpdateEmptyState();
            FinishViewModel.Instance.CompletedDownloads.CollectionChanged += (_, _) => UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (EmptyFinishPanel != null)
                EmptyFinishPanel.Visibility = FinishViewModel.Instance.CompletedDownloads.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void CardOpenFile_Click(object sender, MouseButtonEventArgs e)
        {
            var task = FindTaskFromSender(sender);
            if (task == null) return;
            try
            {
                if (!string.IsNullOrEmpty(task.SavePath) && File.Exists(task.SavePath))
                    Process.Start(new ProcessStartInfo(task.SavePath) { UseShellExecute = true });
            }
            catch { }
        }

        private void CardOpenFolder_Click(object sender, MouseButtonEventArgs e)
        {
            var task = FindTaskFromSender(sender);
            if (task == null) return;
            try
            {
                string folder = !string.IsNullOrEmpty(task.SavePath) ? Path.GetDirectoryName(task.SavePath) ?? "" : task.SaveFolder;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start("explorer.exe", folder);
            }
            catch { }
        }

        private void CardRemove_Click(object sender, MouseButtonEventArgs e)
        {
            var task = FindTaskFromSender(sender);
            if (task == null) return;
            if (MessageBox.Show($"Remove '{task.Filename}' from history?", "MAC-1", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                DataService.Instance.RemoveDownload(task.Id);
                DataService.Instance.NotifyStatsChanged();
            }
        }

        private static DownloadTask? FindTaskFromSender(object sender)
        {
            var element = sender as DependencyObject;
            while (element != null)
            {
                if (element is Border border && border.Tag is DownloadTask task)
                    return task;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private void CardMore_Click(object sender, MouseButtonEventArgs e)
        {
            var task = FindTaskFromSender(sender);
            if (task == null) return;

            SharedContextPopup.BuildForTask(task);
            SharedContextPopup.SetOwnerPopup(SharedPopup);
            SharedPopup.PlacementTarget = sender as FrameworkElement;
            SharedPopup.IsOpen = true;
        }

        private void SharedPopup_Closed(object sender, EventArgs e)
        {
        }
    }
}
