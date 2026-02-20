using System.Collections.ObjectModel;
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

        [ObservableProperty]
        private ObservableCollection<DevLogItem> _logs = new();

        [ObservableProperty]
        private string _newLogTitle = string.Empty;

        public DevLogViewModel()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            var loadedLogs = await _dataService.LoadLogsAsync();
            foreach (var log in loadedLogs)
            {
                Logs.Add(log);
            }
        }

        [RelayCommand]
        private async Task AddLogAsync()
        {
            if (string.IsNullOrWhiteSpace(NewLogTitle)) return;

            var newItem = new DevLogItem
            {
                Title = NewLogTitle,
                IsCompleted = true // 默认录入即完成
            };

            Logs.Insert(0, newItem); // 插在最前面
            NewLogTitle = string.Empty;

            await _dataService.SaveLogsAsync(new System.Collections.Generic.List<DevLogItem>(Logs));

            // 发送粒子爆发请求给 MainWindow
            WeakReferenceMessenger.Default.Send(new DevLogCompletedMessage(newItem.Title));
        }

        [RelayCommand]
        private async Task DeleteLogAsync(DevLogItem item)
        {
            if (item != null && Logs.Contains(item))
            {
                Logs.Remove(item);
                await _dataService.SaveLogsAsync(new System.Collections.Generic.List<DevLogItem>(Logs));
            }
        }
    }
}