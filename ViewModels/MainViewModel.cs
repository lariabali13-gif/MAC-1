using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private static readonly Lazy<MainViewModel> _instance = new(() => new MainViewModel());
        public static MainViewModel Instance => _instance.Value;

        public ObservableCollection<DownloadTask> Downloads => DataService.Instance.Downloads;

        private int _totalDownloads;
        private int _completedDownloads;
        private int _activeDownloads;
        private int _pausedDownloads;
        private int _failedDownloads;
        private int _allCount;
        private string _totalSizeDisplay = "0 B";
        private string _diskSpeed = "0 B/s";
        private string _activeCount = "0";
        private string _activeStatusText = "IDLE";
        private string _activeFilter = "All";

        public int TotalDownloads { get => _totalDownloads; set => SetProperty(ref _totalDownloads, value); }
        public int CompletedDownloads { get => _completedDownloads; set => SetProperty(ref _completedDownloads, value); }
        public int ActiveDownloads { get => _activeDownloads; set => SetProperty(ref _activeDownloads, value); }
        public int PausedDownloads { get => _pausedDownloads; set => SetProperty(ref _pausedDownloads, value); }
        public int FailedDownloads { get => _failedDownloads; set => SetProperty(ref _failedDownloads, value); }
        public int AllCount { get => _allCount; set => SetProperty(ref _allCount, value); }
        public string TotalSizeDisplay { get => _totalSizeDisplay; set => SetProperty(ref _totalSizeDisplay, value); }
        public string DiskSpeed { get => _diskSpeed; set => SetProperty(ref _diskSpeed, value); }
        public string ActiveCount { get => _activeCount; set => SetProperty(ref _activeCount, value); }
        public string ActiveStatusText { get => _activeStatusText; set => SetProperty(ref _activeStatusText, value); }
        public string ActiveFilter
        {
            get => _activeFilter;
            set
            {
                SetProperty(ref _activeFilter, value);
                RefreshFilteredDownloads();
            }
        }

        public ObservableCollection<DownloadTask> FilteredDownloads { get; } = new();

        private MainViewModel()
        {
            DataService.Instance.StatsChanged += () => UpdateStats();
            DataService.Instance.Downloads.CollectionChanged += (_, _) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStats();
                    RefreshFilteredDownloads();
                });
            };
            UpdateStats();
            RefreshFilteredDownloads();
        }

        private void UpdateStats()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var all = DataService.Instance.Downloads;
                TotalDownloads = all.Count;
                CompletedDownloads = all.Count(d => d.State == DownloadState.Completed);
                ActiveDownloads = all.Count(d => d.State == DownloadState.Downloading);
                PausedDownloads = all.Count(d => d.State == DownloadState.Paused);
                FailedDownloads = all.Count(d => d.State == DownloadState.Failed);
                AllCount = all.Count;
                TotalSizeDisplay = DownloadTask.FormatSize(DataService.Instance.TotalSizeDownloaded);
                ActiveCount = DataService.Instance.ActiveDownloads.ToString();
                ActiveStatusText = DataService.Instance.ActiveDownloads > 0
                    ? $"DOWNLOADING ({DataService.Instance.ActiveDownloads} FILES ACTIVE)"
                    : "IDLE";
            });
        }

        public void RefreshFilteredDownloads()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var all = DataService.Instance.Downloads;
                FilteredDownloads.Clear();

                // In Downloads section, exclude completed items (they belong in Finish section)
                var active = all.Where(d => d.State != DownloadState.Completed);

                var filtered = ActiveFilter switch
                {
                    "All" => active,
                    "Downloading" => active.Where(d => d.State == DownloadState.Downloading),
                    "Paused" => active.Where(d => d.State == DownloadState.Paused),
                    "Failed" => active.Where(d => d.State == DownloadState.Failed),
                    _ => active
                };

                foreach (var task in filtered)
                    FilteredDownloads.Add(task);
            });
        }

        public ObservableCollection<DownloadTask> GetActiveDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Downloading || d.State == DownloadState.Paused));
        }

        public ObservableCollection<DownloadTask> GetCompletedDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Completed));
        }

        public ObservableCollection<DownloadTask> GetQueuedDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Queued || d.State == DownloadState.Waiting));
        }

        public ObservableCollection<DownloadTask> GetFailedDownloads()
        {
            return new ObservableCollection<DownloadTask>(
                Downloads.Where(d => d.State == DownloadState.Failed));
        }
    }
}
