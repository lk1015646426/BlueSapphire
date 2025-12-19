using Windows.Storage;
using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BlueSapphire.Models
{
    /// <summary>
    /// 用于去重结果展示的列表项模型
    /// </summary>
    public partial class DuplicateItem : ObservableObject
    {
        // [核心修复] 使用 required 修饰符，解决编译器警告
        // 或者是 'StorageFile?' (可空)
        public StorageFile? File { get; init; }

        public bool IsSeparator { get; init; }
        public bool IsKeepSuggestion { get; init; }

        [ObservableProperty]
        private bool _isChecked;

        public string DisplayName => File?.Name ?? "Group Separator";
        public string DateString => File != null ? File.DateCreated.ToString("g") : "";

        // 控制 UI 显示的属性
        public Visibility SeparatorVisibility => IsSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CheckBoxVisibility => IsSeparator ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SuggestionVisibility => IsKeepSuggestion ? Visibility.Visible : Visibility.Collapsed;

        public DuplicateItem(StorageFile? file, bool isKeepSuggestion = false)
        {
            File = file;
            IsSeparator = false;
            IsKeepSuggestion = isKeepSuggestion;
            // 建议保留的不勾选删除，不保留的默认勾选删除
            IsChecked = !isKeepSuggestion;
        }

        // 私有构造函数，用于创建分隔符
        private DuplicateItem()
        {
            IsSeparator = true;
        }

        public static DuplicateItem CreateSeparator()
        {
            return new DuplicateItem();
        }
    }
}