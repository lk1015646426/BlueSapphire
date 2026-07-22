using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

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

        private string _fullContent = string.Empty;
        public string FullContent
        {
            get => _fullContent;
            set => SetProperty(ref _fullContent, value);
        }

        private string _version = "1.0.0";
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        // 核心修复：数据直接存储正规中文，默认值为“常规迭代”
        private string _updateLevel = "常规迭代";
        public string UpdateLevel
        {
            get => _updateLevel;
            set
            {
                if (SetProperty(ref _updateLevel, value))
                {
                    OnPropertyChanged(nameof(IsMajorRelease));
                    OnPropertyChanged(nameof(VersionTypeTag));
                    OnPropertyChanged(nameof(VersionGlowBrush));
                    OnPropertyChanged(nameof(CardBackgroundBrush));
                    OnPropertyChanged(nameof(CardBorderBrush));
                }
            }
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

        // =========================================================
        // 基于精准的中文词汇进行判定，抛弃自动猜测逻辑
        // =========================================================

        [JsonIgnore]
        public string DisplayTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        [JsonIgnore]
        public bool IsMajorRelease => UpdateLevel == "核心跃迁";

        [JsonIgnore]
        public string VersionTypeTag => UpdateLevel; // 直接返回选择的中文词汇（核心跃迁 / 常规迭代）

        [JsonIgnore]
        public SolidColorBrush VersionGlowBrush => IsMajorRelease
            ? GetThemeBrush("AccentReview", Color.FromArgb(255, 251, 191, 36))
            : GetThemeBrush("AccentPrimary", Color.FromArgb(255, 229, 150, 91));

        [JsonIgnore]
        public SolidColorBrush CardBackgroundBrush => IsMajorRelease
            ? GetThemeBrush("AccentReviewBg", Color.FromArgb(20, 251, 191, 36))
            : GetThemeBrush("AccentPrimaryBg", Color.FromArgb(28, 184, 79, 43));

        [JsonIgnore]
        public SolidColorBrush CardBorderBrush => IsMajorRelease
            ? GetThemeBrush("AccentReview", Color.FromArgb(255, 251, 191, 36))
            : GetThemeBrush("AccentPrimary", Color.FromArgb(255, 229, 150, 91));

        private static SolidColorBrush GetThemeBrush(string key, Color fallback)
        {
            if (Application.Current?.Resources.TryGetValue(key, out object? value) == true &&
                value is SolidColorBrush brush)
            {
                return brush;
            }
            return new SolidColorBrush(fallback);
        }
    }
}
