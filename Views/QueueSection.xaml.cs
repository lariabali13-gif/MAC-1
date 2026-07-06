using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MAC_1.Models;
using MAC_1.Services;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class QueueSection : UserControl
    {
        public QueueSection()
        {
            InitializeComponent();
            DataContext = QueueViewModel.Instance;
        }

        private void QueuePauseResume_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DownloadTask task)
            {
                if (task.State == DownloadState.Downloading)
                    task.State = DownloadState.Paused;
                else if (task.State == DownloadState.Paused || task.State == DownloadState.Queued)
                    task.State = DownloadState.Downloading;

                QueueViewModel.Instance.Refresh();
            }
        }

        private void QueueRemove_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DownloadTask task)
            {
                DataService.Instance.RemoveDownload(task.Id);
                QueueViewModel.Instance.Refresh();
            }
        }
    }
}
