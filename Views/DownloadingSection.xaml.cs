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
    public partial class DownloadingSection : UserControl
    {
        public DownloadingSection()
        {
            InitializeComponent();
            DataContext = MainViewModel.Instance;
            this.Loaded += (_, _) => { UpdateFilterTabs(); UpdateEmptyState(); };
            MainViewModel.Instance.FilteredDownloads.CollectionChanged += (_, _) => UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (EmptyDownloadsPanel != null)
                EmptyDownloadsPanel.Visibility = MainViewModel.Instance.FilteredDownloads.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void FilterTab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string filter)
            {
                MainViewModel.Instance.ActiveFilter = filter;
                UpdateFilterTabs();
            }
        }

        private void UpdateFilterTabs()
        {
            string current = MainViewModel.Instance.ActiveFilter;
            SetTabStyle(TabAll, "All", current);
            SetTabStyle(TabDownloading, "Downloading", current);
            SetTabStyle(TabPaused, "Paused", current);
            SetTabStyle(TabFailed, "Failed", current);
        }

        private void SetTabStyle(Border tab, string filterName, string activeFilter)
        {
            bool isActive = filterName == activeFilter;
            tab.Background = isActive
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("Transparent"));

            if (tab.Child is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is TextBlock tb)
                    {
                        tb.Foreground = isActive
                            ? Brushes.White
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                    }
                    if (child is Border countBorder && countBorder.Child is TextBlock countTb)
                    {
                        countBorder.Background = isActive
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                        countTb.Foreground = isActive
                            ? Brushes.White
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                    }
                }
            }
        }

        private async void CardPause_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DownloadTask task)
            {
                try
                {
                    var httpClient = Utils.ServiceLocator.HttpClient;

                    if (task.State == DownloadState.Downloading)
                    {
                        var content = ResumeRequestBuilder.BuildPauseContent(task);
                        await httpClient.PostAsync("http://127.0.0.1:57575/api/pause-download", content);
                        task.State = DownloadState.Paused;
                        task.Speed = "Paused";
                    }
                    else if (task.State == DownloadState.Paused || task.State == DownloadState.Failed)
                    {
                        var content = ResumeRequestBuilder.BuildResumeContent(task);
                        await httpClient.PostAsync("http://127.0.0.1:57575/api/resume-download", content);
                        task.State = DownloadState.Downloading;
                        task.Speed = "0 B/s";
                    }
                    DataService.Instance.NotifyStatsChanged();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CardRemove_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DownloadTask task)
            {
                if (MessageBox.Show($"Delete '{task.Filename}' and its file?", "MAC-1", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        var httpClient = Utils.ServiceLocator.HttpClient;
                        var request = new { sessionId = task.ServiceSessionId ?? task.Id };
                        var json = System.Text.Json.JsonSerializer.Serialize(request);
                        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        await httpClient.PostAsync("http://127.0.0.1:57575/api/cancel-download", content);
                    }
                    catch { }

                    // Delete the file from disk
                    try
                    {
                        string filePath = Path.Combine(task.SaveFolder, task.Filename);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                    }
                    catch { }

                    // Remove from collection
                    DataService.Instance.RemoveDownload(task.Id);
                    DataService.Instance.NotifyStatsChanged();
                }
            }
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

        private static DownloadTask? FindTaskFromSender(object sender)
        {
            var element = sender as System.Windows.DependencyObject;
            while (element != null)
            {
                if (element is Border border && border.Tag is DownloadTask task)
                    return task;
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
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
