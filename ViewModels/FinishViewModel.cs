using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using MAC_1.Models;
using MAC_1.Services;

namespace MAC_1.ViewModels
{
    public class FinishViewModel : ViewModelBase
    {
        private static readonly Lazy<FinishViewModel> _instance = new(() => new FinishViewModel());
        public static FinishViewModel Instance => _instance.Value;

        public ObservableCollection<DownloadTask> CompletedDownloads { get; } = new();

        private int _completedCount;
        public int CompletedCount { get => _completedCount; set => SetProperty(ref _completedCount, value); }

        private FinishViewModel()
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
                var completed = DataService.Instance.Downloads
                    .Where(d => d.State == DownloadState.Completed)
                    .OrderByDescending(d => d.CompletedTime)
                    .ToList();

                CompletedDownloads.Clear();
                foreach (var task in completed)
                    CompletedDownloads.Add(task);
                CompletedCount = completed.Count;
            });
        }
    }
}
