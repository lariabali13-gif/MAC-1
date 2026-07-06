using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MAC_1.Models;
using MAC_1.Services;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class DashboardSection : UserControl
    {
        public DashboardSection()
        {
            InitializeComponent();
            DataContext = DashboardViewModel.Instance;
            this.Loaded += DashboardSection_Loaded;
        }

        private void DashboardSection_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
            fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        private async void DashboardPause_Click(object sender, MouseButtonEventArgs e)
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
                catch { }
            }
        }

        private async void DashboardCancel_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DownloadTask task)
            {
                try
                {
                    var httpClient = Utils.ServiceLocator.HttpClient;
                    var request = new { sessionId = task.ServiceSessionId ?? task.Id };
                    var json = JsonSerializer.Serialize(request);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    await httpClient.PostAsync("http://127.0.0.1:57575/api/cancel-download", content);
                }
                catch { }

                task.State = DownloadState.Paused;
                task.Speed = "Paused";
                DataService.Instance.NotifyStatsChanged();
            }
        }

        private void DashboardOpenFile_Click(object sender, MouseButtonEventArgs e)
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

        private void DashboardOpenFolder_Click(object sender, MouseButtonEventArgs e)
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
    }
}
