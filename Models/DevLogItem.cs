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

        // 新增：用于存储 TXT 完整内容的底层数据源
        private string _fullContent = string.Empty;
        public string FullContent
        {
            get => _fullContent;
            set => SetProperty(ref _fullContent, value);
        }

        private string _version = "v0.6.0";
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        private DevLogStatus _status = DevLogStatus.Completed;
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
                    OnPropertyChanged(nameof(DisplayTime));
                }
            }
        }

        public string DisplayTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    }
}