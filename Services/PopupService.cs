using System;
using System.IO;
using System.Windows;
using MAC_1.Models;
using MAC_1.Views;

namespace MAC_1.Services
{
    public class PopupService
    {
        private static readonly Lazy<PopupService> _instance = new(() => new PopupService());
        public static PopupService Instance => _instance.Value;

        private DownloadPopup? _activePopup;
        private string? _activePopupSessionId;

        public event Action<DownloadTask>? DownloadStarted;
        public event Action? PopupClosed;

        private PopupService() { }

        public void ShowDownloadPopup(DownloadTask task)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CloseActivePopup();
                _activePopup = new DownloadPopup(task);
                _activePopup.DownloadStarted += OnDownloadStarted;
                _activePopup.Closed += (_, _) => { _activePopup = null; _activePopupSessionId = null; PopupClosed?.Invoke(); };
                _activePopup.Show();
                _activePopupSessionId = task.ServiceSessionId;
            });
        }

        public void UpdateProgress(string sessionId, double progress, long bytesDownloaded, long totalBytes, double speed)
        {
            if (_activePopup == null || _activePopupSessionId != sessionId) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _activePopup.UpdateProgress(progress, bytesDownloaded, totalBytes, speed);
            });
        }

        public void DownloadComplete(string sessionId, string savePath)
        {
            if (_activePopup == null || _activePopupSessionId != sessionId) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _activePopup.ShowCompleted(savePath);
            });
        }

        public void DownloadFailed(string sessionId, string error)
        {
            if (_activePopup == null || _activePopupSessionId != sessionId) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _activePopup.ShowFailed(error);
            });
        }

        public void DownloadPaused(string sessionId, long bytesDownloaded)
        {
            if (_activePopup == null || _activePopupSessionId != sessionId) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _activePopup.ShowPaused(bytesDownloaded);
            });
        }

        public void CloseActivePopup()
        {
            _activePopup?.Close();
            _activePopup = null;
            _activePopupSessionId = null;
        }

        public bool HasActivePopup => _activePopup != null;

        private void OnDownloadStarted(DownloadTask task)
        {
            DataService.Instance.AddDownload(task);
            DownloadStarted?.Invoke(task);
        }
    }
}
