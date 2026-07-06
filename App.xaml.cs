using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1
{
    public partial class App : Application
    {
        private static readonly string _logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MAC-1", "wpf-app.log");

        private static bool _isPopupMode;
        private static Window? _mainWindow;
        private static Views.DownloadPopup? _popupModePopup;
        private static string? _popupModeSessionId;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _isPopupMode = e.Args.Contains("--popup");

            // Initialize services
            var dataService = DataService.Instance;
            var settingsService = SettingsService.Instance;
            var popupService = PopupService.Instance;

            // Start pipe client
            var pipeClient = new PipeClient();
            pipeClient.SessionReceived += OnSessionReceived;
            pipeClient.ProgressReceived += OnProgressReceived;
            pipeClient.DownloadCompleted += OnDownloadCompleted;
            pipeClient.DownloadFailed += OnDownloadFailed;
            pipeClient.DownloadPaused += OnDownloadPaused;
            pipeClient.Start();

            if (_isPopupMode)
            {
                Log("App started in POPUP mode (no main window)");
                // In popup mode, don't show MainWindow — wait for session via pipe
            }
            else
            {
                // Normal mode — show MainWindow
                _mainWindow = new Views.MainWindow();
                _mainWindow.Closed += (_, _) => Shutdown();
                _mainWindow.Show();
                Log("App started, pipe client connecting...");
            }
        }

        private void OnSessionReceived(string sessionJson)
        {
            try
            {
                var session = JsonSerializer.Deserialize<SessionData>(sessionJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (session == null || string.IsNullOrEmpty(session.Url)) return;

                Log($"Session received: id={session.SessionId}, filename={session.Filename}, size={session.FileSize}");

                Dispatcher.Invoke(() =>
                {
                    // Create DownloadTask from session
                    var task = new DownloadTask
                    {
                        Url = session.Url,
                        Filename = session.Filename ?? "download",
                        TotalSize = session.FileSize,
                        Category = GuessCategory(session.Filename),
                        ResumeSupported = true,
                        SaveFolder = DataService.Instance.Settings.DefaultSavePath,
                        ServiceSessionId = session.SessionId,
                        SessionHeaders = session.Headers,
                        SessionCookies = session.Cookies,
                        SessionReferrer = session.Referrer,
                        SessionUserAgent = session.UserAgent,
                        SessionMethod = session.Method,
                        SessionMimeType = session.MimeType
                    };

                    if (_isPopupMode)
                    {
                        // Popup mode: show only the download popup window
                        var popup = new Views.DownloadPopup(task);
                        _popupModePopup = popup;
                        _popupModeSessionId = session.SessionId;
                        popup.DownloadStarted += (t) =>
                        {
                            DataService.Instance.AddDownload(t);
                        };
                        popup.Closed += (_, _) =>
                        {
                            _popupModePopup = null;
                            _popupModeSessionId = null;
                            var active = DataService.Instance.Downloads.Count(d =>
                                d.State == DownloadState.Downloading || d.State == DownloadState.Paused);
                            if (active == 0)
                                Shutdown();
                        };
                        popup.Show();
                        Log("Popup mode: showing download popup");
                    }
                    else
                    {
                        // Normal mode: show popup through PopupService
                        PopupService.Instance.ShowDownloadPopup(task);
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Session parse error: {ex.Message}");
            }
        }

        private string GuessCategory(string? filename)
        {
            if (string.IsNullOrEmpty(filename)) return "General";
            string ext = Path.GetExtension(filename).ToLowerInvariant();
            return ext switch
            {
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "Compressed",
                ".exe" or ".msi" or ".dmg" or ".apk" => "Software",
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" => "Documents",
                ".mp3" or ".flac" or ".wav" or ".aac" => "Music",
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "Video",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".svg" => "Images",
                _ => "General"
            };
        }

        private void OnProgressReceived(string progressJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(progressJson);
                var root = doc.RootElement;
                string sessionId = root.GetProperty("sessionId").GetString() ?? "";
                double progress = root.GetProperty("progress").GetDouble();
                long bytesDownloaded = root.GetProperty("bytesDownloaded").GetInt64();
                long totalBytes = root.GetProperty("totalBytes").GetInt64();
                double speed = root.GetProperty("speed").GetDouble();

                Dispatcher.Invoke(() =>
                {
                    // Update the popup through PopupService (normal mode)
                    PopupService.Instance.UpdateProgress(sessionId, progress, bytesDownloaded, totalBytes, speed);

                    // Also update popup in popup mode (direct reference)
                    if (_popupModePopup != null && _popupModeSessionId == sessionId)
                    {
                        _popupModePopup.UpdateProgress(progress, bytesDownloaded, totalBytes, speed);
                    }

                    // Also update the DownloadTask directly so ALL UI (cards, dashboard) see the change
                    var task = DataService.Instance.Downloads.FirstOrDefault(d => d.ServiceSessionId == sessionId);
                    if (task != null)
                    {
                        task.Progress = progress;
                        task.DownloadedSize = bytesDownloaded;
                        if (totalBytes > 0) task.TotalSize = totalBytes;
                        task.Speed = FormatSpeed(speed);
                        if (task.State != DownloadState.Downloading)
                            task.State = DownloadState.Downloading;
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Progress error: {ex.Message}");
            }
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "0 B/s";
            string[] sizes = ["B/s", "KB/s", "MB/s", "GB/s"];
            int i = 0;
            double v = bytesPerSecond;
            while (v >= 1024 && i < sizes.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {sizes[i]}";
        }

        private void OnDownloadCompleted(string completedJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(completedJson);
                var root = doc.RootElement;
                string sessionId = root.GetProperty("sessionId").GetString() ?? "";
                string savePath = root.GetProperty("savePath").GetString() ?? "";
                long bytesDownloaded = root.TryGetProperty("bytesDownloaded", out var bd) ? bd.GetInt64() : 0;
                long fileSize = root.TryGetProperty("fileSize", out var fs) ? fs.GetInt64() : 0;

                Dispatcher.Invoke(() =>
                {
                    var task = DataService.Instance.Downloads.FirstOrDefault(d => d.ServiceSessionId == sessionId);
                    if (task != null)
                    {
                        task.State = DownloadState.Completed;
                        task.SavePath = savePath;
                        if (fileSize > 0) task.TotalSize = fileSize;
                        task.DownloadedSize = task.TotalSize;
                        task.Progress = 100;
                        task.Speed = "Completed";
                        task.CompletedTime = DateTime.Now;
                    }

                    PopupService.Instance.DownloadComplete(sessionId, savePath);

                    // Popup mode: update directly
                    if (_popupModePopup != null && _popupModeSessionId == sessionId)
                    {
                        _popupModePopup.ShowCompleted(savePath);
                    }

                    DataService.Instance.NotifyStatsChanged();
                });

                Log($"Download completed: {savePath}");
            }
            catch (Exception ex)
            {
                Log($"Completed error: {ex.Message}");
            }
        }

        private void OnDownloadFailed(string failedJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(failedJson);
                var root = doc.RootElement;
                string sessionId = root.GetProperty("sessionId").GetString() ?? "";
                string error = root.GetProperty("error").GetString() ?? "Unknown error";
                long bytesDownloaded = root.TryGetProperty("bytesDownloaded", out var bd) ? bd.GetInt64() : 0;
                long totalBytes = root.TryGetProperty("totalBytes", out var tb) ? tb.GetInt64() : 0;

                Dispatcher.Invoke(() =>
                {
                    // Update task state to Failed (not delete)
                    var task = DataService.Instance.Downloads.FirstOrDefault(d => d.ServiceSessionId == sessionId);
                    if (task != null)
                    {
                        task.State = DownloadState.Failed;
                        task.ErrorMessage = error;
                        task.DownloadedSize = bytesDownloaded;
                        if (totalBytes > 0) task.TotalSize = totalBytes;
                        task.Speed = "Failed";
                    }

                    PopupService.Instance.DownloadFailed(sessionId, error);

                    // Popup mode: update directly
                    if (_popupModePopup != null && _popupModeSessionId == sessionId)
                    {
                        _popupModePopup.ShowFailed(error);
                    }
                });

                Log($"Download failed: {error}");
            }
            catch (Exception ex)
            {
                Log($"Failed error: {ex.Message}");
            }
        }

        private void OnDownloadPaused(string pausedJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(pausedJson);
                var root = doc.RootElement;
                string sessionId = root.GetProperty("sessionId").GetString() ?? "";
                long bytesDownloaded = root.GetProperty("bytesDownloaded").GetInt64();

                Dispatcher.Invoke(() =>
                {
                    PopupService.Instance.DownloadPaused(sessionId, bytesDownloaded);

                    // Popup mode: update directly
                    if (_popupModePopup != null && _popupModeSessionId == sessionId)
                    {
                        _popupModePopup.ShowPaused(bytesDownloaded);
                    }
                });

                Log($"Download paused: {sessionId}, bytes={bytesDownloaded}");
            }
            catch (Exception ex)
            {
                Log($"Paused error: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DataService.Instance.SaveDownloads();
            base.OnExit(e);
        }

        private void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }

    // Simple session model for pipe communication
    public class SessionData
    {
        public string? SessionId { get; set; }
        public string Url { get; set; } = "";
        public string FinalUrl { get; set; } = "";
        public string Filename { get; set; } = "";
        public long FileSize { get; set; }
        public string? MimeType { get; set; }
        public string? Method { get; set; }
        public string? Referrer { get; set; }
        public string? UserAgent { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public List<SessionCookie>? Cookies { get; set; }
    }

    public class SessionCookie
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path { get; set; } = "/";
        public bool Secure { get; set; }
    }
}
