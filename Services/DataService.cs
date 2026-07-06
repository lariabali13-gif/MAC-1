using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using MAC_1.Models;

namespace MAC_1.Services
{
    public class DataService
    {
        private static readonly Lazy<DataService> _instance = new(() => new DataService());
        public static DataService Instance => _instance.Value;

        private readonly string _dataFolder;
        private readonly string _categoriesFile;
        private readonly string _settingsFile;
        private readonly string _downloadsFile;

        public ObservableCollection<DownloadTask> Downloads { get; } = new();
        public List<Category> Categories { get; private set; } = new();
        public AppSettings Settings { get; private set; } = new();

        public int TotalDownloads => Downloads.Count;
        public int CompletedDownloads => Downloads.Count(d => d.State == DownloadState.Completed);
        public int ActiveDownloads => Downloads.Count(d => d.State == DownloadState.Downloading);
        public int FailedDownloads => Downloads.Count(d => d.State == DownloadState.Failed);
        public long TotalSizeDownloaded => Downloads.Where(d => d.State == DownloadState.Completed).Sum(d => d.TotalSize);

        public event Action<DownloadTask>? DownloadAdded;
        public event Action<DownloadTask>? DownloadRemoved;
        public event Action? StatsChanged;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public void NotifyStatsChanged()
        {
            StatsChanged?.Invoke();
        }

        private DataService()
        {
            _dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MAC-1");
            _categoriesFile = Path.Combine(_dataFolder, "categories.json");
            _settingsFile = Path.Combine(_dataFolder, "settings.json");
            _downloadsFile = Path.Combine(_dataFolder, "downloads.json");

            Directory.CreateDirectory(_dataFolder);
            LoadCategories();
            LoadSettings();
            LoadDownloads();

            Downloads.CollectionChanged += (_, _) => StatsChanged?.Invoke();
        }

        public void AddDownload(DownloadTask task)
        {
            task.CheckIfArchive();
            Downloads.Add(task);
            task.PropertyChanged += OnDownloadPropertyChanged;
            DownloadAdded?.Invoke(task);
            StatsChanged?.Invoke();
            SaveDownloads();
        }

        private DateTime _lastAutoSave = DateTime.MinValue;

        private void OnDownloadPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Auto-save when critical properties change
            if (e.PropertyName == nameof(DownloadTask.State) ||
                e.PropertyName == nameof(DownloadTask.DownloadedSize) ||
                e.PropertyName == nameof(DownloadTask.Progress))
            {
                // Throttle: save at most once per 3 seconds
                if ((DateTime.Now - _lastAutoSave).TotalSeconds >= 3)
                {
                    _lastAutoSave = DateTime.Now;
                    SaveDownloads();
                }
            }
        }

        public void RemoveDownload(string taskId)
        {
            var task = Downloads.FirstOrDefault(d => d.Id == taskId);
            if (task != null)
            {
                task.PropertyChanged -= OnDownloadPropertyChanged;
                Downloads.Remove(task);
                DownloadRemoved?.Invoke(task);
                StatsChanged?.Invoke();
                SaveDownloads();
            }
        }

        public DownloadTask? GetDownload(string taskId)
            => Downloads.FirstOrDefault(d => d.Id == taskId);

        public Category? GetCategoryByName(string name)
            => Categories.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public Category GetDefaultCategory()
            => Categories.FirstOrDefault(c => c.IsDefault) ?? Categories.First();

        public string GetSavePathForCategory(string categoryName)
        {
            var cat = GetCategoryByName(categoryName);
            return cat?.FolderPath ?? Settings.DefaultSavePath;
        }

        // === DOWNLOADS PERSISTENCE ===

        public void SaveDownloads()
        {
            try
            {
                // Reset speed display for non-active downloads before saving
                foreach (var task in Downloads)
                {
                    if (task.State == DownloadState.Paused)
                        task.Speed = "Paused";
                    else if (task.State == DownloadState.Completed)
                        task.Speed = "Completed";
                    else if (task.State == DownloadState.Failed)
                        task.Speed = task.ErrorMessage ?? "Failed";
                }

                var json = JsonSerializer.Serialize(Downloads.ToList(), _jsonOptions);
                File.WriteAllText(_downloadsFile, json);
            }
            catch { }
        }

        private void LoadDownloads()
        {
            try
            {
                if (File.Exists(_downloadsFile))
                {
                    var json = File.ReadAllText(_downloadsFile);
                    var tasks = JsonSerializer.Deserialize<List<DownloadTask>>(json) ?? new();
                    foreach (var task in tasks)
                    {
                        task.CheckIfArchive();
                        task.PropertyChanged += OnDownloadPropertyChanged;

                        // Fix state on load: anything that was Downloading before app close is now Paused
                        if (task.State == DownloadState.Downloading)
                        {
                            task.State = DownloadState.Paused;
                            task.Speed = "Paused";
                        }

                        // Reset display-only fields
                        if (task.State == DownloadState.Completed)
                        {
                            task.Speed = "Completed";
                            // Ensure completed tasks show full size
                            if (task.TotalSize > 0)
                                task.DownloadedSize = task.TotalSize;
                        }
                        else if (task.State == DownloadState.Paused)
                            task.Speed = "Paused";
                        else if (task.State == DownloadState.Failed)
                            task.Speed = task.ErrorMessage ?? "Failed";

                        Downloads.Add(task);
                    }
                }
            }
            catch { }
        }

        // === CATEGORIES ===

        private void SaveCategories()
        {
            try
            {
                var json = JsonSerializer.Serialize(Categories, _jsonOptions);
                File.WriteAllText(_categoriesFile, json);
            }
            catch { }
        }

        public void AddCategory(Category category)
        {
            Categories.Add(category);
            SaveCategories();
        }

        public void RemoveCategory(string categoryId)
        {
            Categories.RemoveAll(c => c.Id == categoryId);
            SaveCategories();
        }

        // === SETTINGS ===

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(Settings, _jsonOptions);
                File.WriteAllText(_settingsFile, json);
            }
            catch { }
        }

        private void LoadCategories()
        {
            try
            {
                if (File.Exists(_categoriesFile))
                {
                    var json = File.ReadAllText(_categoriesFile);
                    Categories = JsonSerializer.Deserialize<List<Category>>(json) ?? new();
                    return;
                }
            }
            catch { }

            Categories = Category.GetDefaults(Settings.DefaultSavePath);
            SaveCategories();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
                    return;
                }
            }
            catch { }

            Settings = new AppSettings();
            SaveSettings();
        }

        // === BACKWARD COMPAT (old history.json) ===

        [System.Obsolete]
        public void SaveHistory()
        {
            SaveDownloads();
        }
    }
}
