using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1.ViewModels
{
    public class QueueViewModel : ViewModelBase
    {
        private static readonly Lazy<QueueViewModel> _instance = new(() => new QueueViewModel());
        public static QueueViewModel Instance => _instance.Value;

        public ObservableCollection<DownloadTask> QueuedDownloads { get; } = new();

        private int _queuedCount;
        private int _waitingCount;
        private int _activeCount;
        private string _avgSpeedDisplay = "0 B/s";

        public int QueuedCount { get => _queuedCount; set => SetProperty(ref _queuedCount, value); }
        public int WaitingCount { get => _waitingCount; set => SetProperty(ref _waitingCount, value); }
        public int ActiveCount { get => _activeCount; set => SetProperty(ref _activeCount, value); }
        public string AvgSpeedDisplay { get => _avgSpeedDisplay; set => SetProperty(ref _avgSpeedDisplay, value); }

        private QueueViewModel()
        {
            DataService.Instance.StatsChanged += Refresh;
            DataService.Instance.Downloads.CollectionChanged += (_, _) =>
            {
                Application.Current.Dispatcher.Invoke(Refresh);
            };
            Refresh();
        }

        public void Refresh()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var all = DataService.Instance.Downloads;
                var queued = all.Where(d => d.State == DownloadState.Queued || d.State == DownloadState.Waiting)
                               .OrderBy(d => d.StartTime).ToList();

                QueuedCount = queued.Count;
                WaitingCount = queued.Count(d => d.State == DownloadState.Waiting || d.State == DownloadState.Queued);
                ActiveCount = queued.Count(d => d.State == DownloadState.Downloading);

                // Calculate average speed
                var downloading = all.Where(d => d.State == DownloadState.Downloading).ToList();
                if (downloading.Any())
                {
                    double totalSpeed = downloading.Sum(d =>
                    {
                        if (double.TryParse(d.Speed.Replace(" B/s", "").Replace(" KB/s", "").Replace(" MB/s", "").Replace(" GB/s", ""), out double speed))
                        {
                            if (d.Speed.Contains("KB")) speed *= 1024;
                            else if (d.Speed.Contains("MB")) speed *= 1024 * 1024;
                            else if (d.Speed.Contains("GB")) speed *= 1024 * 1024 * 1024;
                            return speed;
                        }
                        return 0;
                    });
                    AvgSpeedDisplay = DownloadTask.FormatSize((long)totalSpeed) + "/s";
                }
                else
                {
                    AvgSpeedDisplay = "0 B/s";
                }

                QueuedDownloads.Clear();
                foreach (var task in queued)
                    QueuedDownloads.Add(task);
            });
        }
    }
}
