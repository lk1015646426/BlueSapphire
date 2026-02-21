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

        public DevLogViewModel()
        {
            _ = InitializeAsync();
        }

        // DevLogViewModel.cs 中的 InitializeAsync 方法
        private async Task InitializeAsync()
        {
            var loadedLogs = await _dataService.LoadLogsAsync();

            // 直接赋值是最安全的，SetProperty 会自动通知 UI 刷新界面
            Logs = new ObservableCollection<DevLogItem>(loadedLogs);

            UpdateHUD();
        }
        public async Task AddNewLogAsync(string title, string description, string version)
        {
            var newItem = new DevLogItem
            {
                Title = title,
                Description = description,
                Version = string.IsNullOrWhiteSpace(version) ? "v0.6.0" : version,
                Status = DevLogStatus.Pending,
                Timestamp = System.DateTime.Now
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
                // 等待 UI 数据移除完毕后立即保存
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
                WeakReferenceMessenger.Default.Send(new DevLogCompletedMessage(item.Title));
            }

            if (isChanged)
            {
                await SaveDataAsync();
            }
        }

        private async Task SaveDataAsync()
        {
            // 保存前更新统计信息
            UpdateHUD();

            // 将当前的 ObservableCollection 转为全新的 List 交给底层保存
            // 确保底层拿到的是当前内存的精确快照，包括列表为空的情况
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