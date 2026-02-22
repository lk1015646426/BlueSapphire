using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.ViewModels
{
    public partial class DevLogViewModel : ObservableObject
    {
        private readonly DevLogDataService _dataService = new();

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

        private int _completedCount = 0;
        public int CompletedCount
        {
            get => _completedCount;
            set => SetProperty(ref _completedCount, value);
        }

        private int _totalCount = 0;
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

        public DevLogViewModel()
        {
            IsEditable = CheckIfUnpackaged();
            _ = InitializeAsync();
        }

        private bool CheckIfUnpackaged()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        private async Task InitializeAsync()
        {
            var loadedLogs = await _dataService.LoadLogsAsync();

            Logs.Clear();
            foreach (var log in loadedLogs)
            {
                Logs.Add(log);
            }
            UpdateHUD();
        }

        public async Task AddNewLogAsync(string title, string description, string version, string updateLevel, string fullContent, DateTime? date = null)
        {
            var newItem = new DevLogItem
            {
                Title = title,
                Description = description,
                Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
                UpdateLevel = string.IsNullOrWhiteSpace(updateLevel) ? "常规迭代" : updateLevel, // 保障必定为中文分级
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
            if (item != null && Logs.Contains(item))
            {
                Logs.Remove(item);
                await SaveDataAsync();
            }
        }

        [RelayCommand]
        private async Task AdvanceStatusAsync(DevLogItem item)
        {
            if (item == null) return;
            bool isChanged = false;

            if (item.Status == DevLogStatus.Pending)
            {
                item.Status = DevLogStatus.InProgress;
                isChanged = true;
            }
            else if (item.Status == DevLogStatus.InProgress)
            {
                item.Status = DevLogStatus.Completed;
                isChanged = true;
            }

            if (isChanged) await SaveDataAsync();
        }

        public async Task SaveDataAsync()
        {
            UpdateHUD();
            var snapShot = Logs.ToList();
            await _dataService.SaveLogsAsync(snapShot);
        }

        private void UpdateHUD()
        {
            TotalCount = Logs.Count;
            CompletedCount = Logs.Count(l => l.Status == DevLogStatus.Completed);
            CompletionRate = TotalCount == 0 ? "0%" : $"{(CompletedCount * 100 / TotalCount)}%";
        }
    }
}