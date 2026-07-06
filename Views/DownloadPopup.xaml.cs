using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MAC_1.Models;
using MAC_1.Services;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class DownloadPopup : Window
    {
        private readonly DownloadTask _task;
        private readonly DownloadViewModel _viewModel;
        private DispatcherTimer? _progressTimer;

        public event Action<DownloadTask>? DownloadStarted;

        private const string NA = "Not Available";

        public DownloadPopup(DownloadTask task)
        {
            InitializeComponent();
            _task = task;
            _viewModel = new DownloadViewModel(task);

            PopulateAllFields();
            LoadSavedCategories();

            task.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadTask.State))
                    Dispatcher.Invoke(OnStateChanged);
            };
        }

        private string S(string? value, string fallback = "Not Available")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private string FormatSize(long bytes)
        {
            return bytes > 0 ? DownloadTask.FormatSize(bytes) : "Not Available";
        }

        private void PopulateAllFields()
        {
            // === FILE INFO ===
            string filename = S(_task.Filename, "download");
            string url = S(_task.Url, "Not Available");
            long fileSize = _task.TotalSize;

            FileNameText.Text = filename;
            FileSizeText.Text = FormatSize(fileSize);
            UrlText.Text = url;
            SavePathText.Text = _task.SaveFolder;

            // === DESCRIPTION ===
            string descParts = "";
            if (!string.IsNullOrEmpty(_task.Category)) descParts += $"Category: {_task.Category}";
            if (!string.IsNullOrEmpty(_task.SessionMimeType)) descParts += (descParts.Length > 0 ? " | " : "") + $"MIME: {_task.SessionMimeType}";
            DescriptionBox.Text = descParts;

            // === INFO CARD ===
            int connections = DataService.Instance.Settings.DefaultConnections;
            InfoFileSize.Text = FormatSize(fileSize);
            InfoConnections.Text = connections.ToString();

            if (fileSize > 0)
            {
                InfoParts.Text = connections.ToString();
                InfoStartPosition.Text = "0 Bytes";
            }
            else
            {
                InfoParts.Text = NA;
                InfoStartPosition.Text = NA;
            }

            InfoResumeSupport.Text = _task.ResumeSupported ? "Yes" : "No";
            InfoResumeSupport.Foreground = _task.ResumeSupported
                ? (Brush)FindResource("Success")
                : (Brush)FindResource("TextMuted");

            InfoEstTime.Text = NA;

            // === DISK INFO ===
            PopulateDiskInfo();
        }

        private void PopulateDiskInfo()
        {
            try
            {
                string savePath = _task.SaveFolder;
                if (!string.IsNullOrEmpty(savePath) && Directory.Exists(savePath))
                {
                    var driveInfo = new DriveInfo(savePath);
                    InfoDiskSpace.Text = DownloadTask.FormatSize(driveInfo.TotalSize);
                    InfoFreeSpace.Text = DownloadTask.FormatSize(driveInfo.AvailableFreeSpace);
                }
                else
                {
                    InfoDiskSpace.Text = "Not Available";
                    InfoFreeSpace.Text = "Not Available";
                }
            }
            catch
            {
                InfoDiskSpace.Text = "Not Available";
                InfoFreeSpace.Text = "Not Available";
            }
        }

        private void LoadSavedCategories()
        {
            string categoriesFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MAC-1", "categories.json");

            if (!File.Exists(categoriesFile)) return;

            try
            {
                var json = File.ReadAllText(categoriesFile);
                var categories = System.Text.Json.JsonSerializer.Deserialize<List<CategoryName>>(json);
                if (categories != null)
                {
                    foreach (var cat in categories)
                    {
                        if (!string.IsNullOrWhiteSpace(cat.Name))
                            CategoryCombo.Items.Add(cat.Name);
                    }
                }
            }
            catch { }
        }

        private void OnStateChanged()
        {
            switch (_task.State)
            {
                case DownloadState.Downloading:
                    ShowState2();
                    break;
                case DownloadState.Completed:
                    ShowState3();
                    break;
                case DownloadState.Failed:
                    // Stay on state2 (downloading UI) but show error
                    // User can click Retry
                    break;
            }
        }

        private void ShowState2()
        {
            State1Panel.Visibility = Visibility.Collapsed;
            State2Panel.Visibility = Visibility.Visible;
            State3Panel.Visibility = Visibility.Collapsed;
        }

        private void ShowState3()
        {
            State1Panel.Visibility = Visibility.Collapsed;
            State2Panel.Visibility = Visibility.Collapsed;
            State3Panel.Visibility = Visibility.Visible;
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            // Disable button to prevent double-click
            StartDownloadBtn.IsEnabled = false;

            // Call service to start download
            try
            {
                var httpClient = Utils.ServiceLocator.HttpClient;
                var request = new
                {
                    sessionId = _task.ServiceSessionId ?? _task.Id,
                    savePath = Path.Combine(_task.SaveFolder, _task.Filename)
                };
                var json = System.Text.Json.JsonSerializer.Serialize(request);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("http://127.0.0.1:57575/api/start-download", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _task.State = DownloadState.Downloading;
                    ShowState2();
                    DownloadStarted?.Invoke(_task);
                }
                else
                {
                    MessageBox.Show($"Failed to start download: {responseBody}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error);
                    StartDownloadBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection error: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error);
                StartDownloadBtn.IsEnabled = true;
            }
        }

        private async void PauseResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var httpClient = Utils.ServiceLocator.HttpClient;

                if (_task.State == DownloadState.Downloading)
                {
                    var content = ResumeRequestBuilder.BuildPauseContent(_task);
                    await httpClient.PostAsync("http://127.0.0.1:57575/api/pause-download", content);
                    _task.State = DownloadState.Paused;
                    _task.Speed = "Paused";
                    DataService.Instance.NotifyStatsChanged();
                }
                else if (_task.State == DownloadState.Paused || _task.State == DownloadState.Failed)
                {
                    var content = ResumeRequestBuilder.BuildResumeContent(_task);
                    await httpClient.PostAsync("http://127.0.0.1:57575/api/resume-download", content);
                    _task.State = DownloadState.Downloading;
                    _task.Speed = "0 B/s";
                    DataService.Instance.NotifyStatsChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _progressTimer?.Stop();

            try
            {
                // Cancel on server (which pauses the download)
                var httpClient = Utils.ServiceLocator.HttpClient;
                var request = new { sessionId = _task.ServiceSessionId ?? _task.Id };
                var json = System.Text.Json.JsonSerializer.Serialize(request);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                await httpClient.PostAsync("http://127.0.0.1:57575/api/cancel-download", content);
            }
            catch { }

            // Set state to paused (keep data, don't remove)
            _task.State = DownloadState.Paused;
            DataService.Instance.NotifyStatsChanged();
            this.Close();
        }

        public void UpdateFileSizeFromExtension(long fileSize)
        {
            if (fileSize > 0)
            {
                _task.TotalSize = fileSize;
                FileSizeText.Text = FormatSize(fileSize);
                InfoFileSize.Text = FormatSize(fileSize);
            }
        }

        public void UpdateProgress(double progress, long bytesDownloaded, long totalBytes, double speed)
        {
            // Cap progress at 100%
            progress = Math.Min(100.0, progress);
            _task.Progress = progress;
            _task.DownloadedSize = bytesDownloaded;
            _task.TotalSize = totalBytes;
            _task.Speed = FormatSpeed(speed);

            try
            {
                // Progress bar
                DownloadProgressBar.Value = progress;

                // Big percentage text (in circle)
                var percentText = FindName("PercentText") as System.Windows.Controls.TextBlock;
                if (percentText != null) percentText.Text = $"{progress:F1}%";

                // Downloaded size + percentage in info area
                var downloadedRun = FindName("DownloadedRun") as System.Windows.Documents.Run;
                if (downloadedRun != null) downloadedRun.Text = FormatSize(bytesDownloaded);

                var percentText2 = FindName("PercentText2") as System.Windows.Documents.Run;
                if (percentText2 != null) percentText2.Text = $"{progress:F1}%";

                var sizeDetailRun = FindName("SizeDetailRun") as System.Windows.Documents.Run;
                if (sizeDetailRun != null) sizeDetailRun.Text = FormatSize(totalBytes);

                var speedText = FindName("SpeedText") as System.Windows.Documents.Run;
                if (speedText != null) speedText.Text = FormatSpeed(speed);

                var avgSpeedStat = FindName("AvgSpeedStat") as System.Windows.Documents.Run;
                if (avgSpeedStat != null) avgSpeedStat.Text = FormatSpeed(speed);

                var speedStat = FindName("SpeedStat") as System.Windows.Documents.Run;
                if (speedStat != null) speedStat.Text = FormatSpeed(speed);

                double eta = speed > 0 && totalBytes > 0 ? (totalBytes - bytesDownloaded) / speed : 0;
                var timeLeftText = FindName("TimeLeftText") as System.Windows.Documents.Run;
                if (timeLeftText != null) timeLeftText.Text = FormatTime(eta);
                var timeLeftStat = FindName("TimeLeftStat") as System.Windows.Documents.Run;
                if (timeLeftStat != null) timeLeftStat.Text = FormatTime(eta);
            }
            catch { }
        }

        public void ShowCompleted(string savePath)
        {
            _task.State = DownloadState.Completed;
            _task.SavePath = savePath;

            // Update completed state fields
            try
            {
                var completedFileName = FindName("CompletedFileName") as System.Windows.Controls.TextBlock;
                if (completedFileName != null) completedFileName.Text = _task.Filename;

                var completedFullSizeText = FindName("CompletedFullSizeText") as System.Windows.Controls.TextBlock;
                if (completedFullSizeText != null) completedFullSizeText.Text = FormatSize(_task.TotalSize);

                var completedTimeText = FindName("CompletedTimeText") as System.Windows.Controls.TextBlock;
                if (completedTimeText != null) completedTimeText.Text = "--";
            }
            catch { }

            ShowState3();
        }

        public void ShowFailed(string error)
        {
            _task.State = DownloadState.Failed;
            _task.ErrorMessage = error;
            _task.Speed = "Failed";

            try
            {
                var speedText = FindName("SpeedText") as System.Windows.Documents.Run;
                if (speedText != null) speedText.Text = "FAILED";

                var speedStat = FindName("SpeedStat") as System.Windows.Documents.Run;
                if (speedStat != null) speedStat.Text = "FAILED";
            }
            catch { }
        }

        public void ShowPaused(long bytesDownloaded)
        {
            _task.DownloadedSize = bytesDownloaded;
            _task.State = DownloadState.Paused;

            // Update UI to show paused state
            try
            {
                var downloadedRun = FindName("DownloadedRun") as System.Windows.Documents.Run;
                if (downloadedRun != null) downloadedRun.Text = FormatSize(bytesDownloaded);

                var speedText = FindName("SpeedText") as System.Windows.Documents.Run;
                if (speedText != null) speedText.Text = "Paused";

                var speedStat = FindName("SpeedStat") as System.Windows.Documents.Run;
                if (speedStat != null) speedStat.Text = "Paused";
            }
            catch { }
        }

        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0 B/s";
            string[] sizes = ["B/s", "KB/s", "MB/s", "GB/s"];
            int i = 0;
            double v = bytesPerSecond;
            while (v >= 1024 && i < sizes.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {sizes[i]}";
        }

        private string FormatTime(double seconds)
        {
            if (seconds <= 0) return "--:--";
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins:D2}:{secs:D2}";
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            var popup = new NewCategoryPopup();
            popup.ShowDialog();
        }

        private void DownloadLater_Click(object sender, RoutedEventArgs e)
        {
            _task.State = DownloadState.Queued;
            DataService.Instance.AddDownload(_task);
            MessageBox.Show("Download added to queue!", "MAC-1", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private void HideDetails_Click(object sender, RoutedEventArgs e) { }
        private void Stop_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_task.SavePath) && File.Exists(_task.SavePath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_task.SavePath) { UseShellExecute = true });
            }
            catch { }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_task.SaveFolder) && Directory.Exists(_task.SaveFolder))
                    System.Diagnostics.Process.Start("explorer.exe", _task.SaveFolder);
            }
            catch { }
        }

        private void OpenWith_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_task.SavePath) && File.Exists(_task.SavePath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {_task.SavePath}"));
            }
            catch { }
        }
    }

    // Helper class for JSON deserialization
    public class CategoryName
    {
        public string Name { get; set; } = "";
    }
}
