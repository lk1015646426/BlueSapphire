using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlueSapphire.Models
{
    public enum DevLogStatus
    {
        Pending,
        InProgress,
        Completed
    }

    public partial class DevLogItem : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private string _version = "v0.6.0";
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        private DevLogStatus _status = DevLogStatus.Pending;
        public DevLogStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private DateTime _timestamp = DateTime.Now;
        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (SetProperty(ref _timestamp, value))
                {
                    // 当时间改变时，同时通知界面更新格式化后的文本
                    OnPropertyChanged(nameof(DisplayTime));
                }
            }
        }

        // 新增：专门用于 UI 显示的字符串属性，彻底避开 XAML 方法绑定缺陷
        public string DisplayTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    }
}