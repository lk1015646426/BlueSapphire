using BlueSapphire.Models;
using BlueSapphire.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace BlueSapphire.ViewModels
{
    public partial class DevLogViewModel : ObservableObject
    {
        private readonly DevLogDataService _dataService;
        private bool _isInitialized;

        private ObservableCollection<DevLogItem> _logs = new();
        public ObservableCollection<DevLogItem> Logs
        {
            get => _logs;
            set => SetProperty(ref _logs, value);
        }

        private string _completionRate = "0%";
        public string CompletionRate
        {
            get => _completionRate;
            set => SetProperty(ref _completionRate, value);
        }

        private int _completedCount;
        public int CompletedCount
        {
            get => _completedCount;
            set => SetProperty(ref _completedCount, value);
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private bool _isEditable;
        public bool IsEditable
        {
            get => _isEditable;
            set => SetProperty(ref _isEditable, value);
        }

        public DevLogViewModel(DevLogDataService dataService)
        {
            _dataService = dataService;
            IsEditable = dataService.CanWrite;
        }

        public async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            var loadedLogs = await _dataService.LoadLogsAsync();

            Logs.Clear();
            foreach (var log in loadedLogs.OrderByDescending(item => item.Timestamp))
            {
                Logs.Add(log);
            }

            UpdateHud();
            _isInitialized = true;
        }

        public async Task AddNewLogAsync(string title, string description, string version, string updateLevel, string fullContent, DateTime? date = null)
        {
            var newItem = new DevLogItem
            {
                Title = title,
                Description = description,
                Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
                UpdateLevel = string.IsNullOrWhiteSpace(updateLevel) ? "常规迭代" : updateLevel,
                FullContent = string.IsNullOrWhiteSpace(fullContent) ? "当前版本暂未录入详细架构文档。" : fullContent,
                Status = DevLogStatus.Completed,
                Timestamp = date ?? DateTime.Now
            };

            Logs.Insert(0, newItem);
            await SaveDataAsync();
        }

        [RelayCommand]
        private async Task DeleteLogAsync(DevLogItem item)
        {
            if (item == null || !Logs.Contains(item))
            {
                return;
            }

            Logs.Remove(item);
            await SaveDataAsync();
        }

        [RelayCommand]
        private async Task AdvanceStatusAsync(DevLogItem item)
        {
            if (item == null)
            {
                return;
            }

            bool changed = false;
            if (item.Status == DevLogStatus.Pending)
            {
                item.Status = DevLogStatus.InProgress;
                changed = true;
            }
            else if (item.Status == DevLogStatus.InProgress)
            {
                item.Status = DevLogStatus.Completed;
                changed = true;
            }

            if (changed)
            {
                await SaveDataAsync();
            }
        }

        public async Task SaveDataAsync()
        {
            UpdateHud();
            await _dataService.SaveLogsAsync(Logs.ToList());
        }

        private void UpdateHud()
        {
            TotalCount = Logs.Count;
            CompletedCount = Logs.Count(item => item.Status == DevLogStatus.Completed);
            CompletionRate = TotalCount == 0 ? "0%" : $"{CompletedCount * 100 / TotalCount}%";
        }
    }
}
