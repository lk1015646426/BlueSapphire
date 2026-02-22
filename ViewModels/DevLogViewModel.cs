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

        public DevLogViewModel()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            var loadedLogs = await _dataService.LoadLogsAsync();

            // 最佳实践：不要重新 new 整个集合，而是清空后逐个添加，这样能确保 UI 绑定绝对不丢失
            Logs.Clear();
            foreach (var log in loadedLogs)
            {
                Logs.Add(log);
            }

            UpdateHUD();
        }

        /// <summary>
        /// 提供给 View 层使用的标准添加方法（包含完整参数）
        /// </summary>
        public async Task AddNewLogAsync(string title, string description, string version, string fullContent, DateTime? date = null)
        {
            var newItem = new DevLogItem
            {
                Title = title,
                Description = description,
                Version = string.IsNullOrWhiteSpace(version) ? "v0.6.0" : version,
                FullContent = string.IsNullOrWhiteSpace(fullContent) ? "暂无详细文档内容。" : fullContent,
                Status = DevLogStatus.Completed,
                Timestamp = date ?? DateTime.Now
            };

            Logs.Insert(0, newItem);
            await SaveDataAsync();
        }

        /// <summary>
        /// 提供给 View 层使用的标准删除命令
        /// </summary>
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
                // 注意：如果使用了弱引用消息，需要确保这里有对应的 Message 类定义
                // WeakReferenceMessenger.Default.Send(new DevLogCompletedMessage(item.Title));
            }

            if (isChanged)
            {
                await SaveDataAsync();
            }
        }

        /// <summary>
        /// 数据持久化保存（已公开，并在增删操作后自动触发）
        /// </summary>
        public async Task SaveDataAsync()
        {
            UpdateHUD();
            // 将当前的 ObservableCollection 转为全新的 List 交给底层保存
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