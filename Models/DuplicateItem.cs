using Windows.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;

namespace BlueSapphire.Models
{
    /// <summary>
    /// 用于去重结果展示的列表项模型
    /// 升级版：支持缩略图显示和懒加载
    /// </summary>
    public partial class DuplicateItem : ObservableObject
    {
        public StorageFile? File { get; init; }

        public bool IsSeparator { get; init; }
        public bool IsKeepSuggestion { get; init; }

        // [修复] 移除 [ObservableProperty]，改用手写属性，完美兼容 AOT 和 WinUI 3 绑定
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        // [修复] 移除 [ObservableProperty]，改用手写属性，完美兼容 AOT 和 WinUI 3 绑定
        private BitmapImage? _thumbnail;
        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        public string DisplayName => File?.Name ?? "Group Separator";
        public string DateString => File != null ? File.DateCreated.ToString("g") : "";
        public string PathString => File?.Path ?? ""; // 用于 ToolTip 显示完整路径

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

        // [核心新增] 异步加载缩略图逻辑
        // 只有当 Item 滚动进入视野时才会被调用
        public async Task LoadThumbnailAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            // 如果没有文件、是分隔符或者已经加载过，则跳过
            if (File == null || IsSeparator || Thumbnail != null) return;

            try
            {
                // 获取 100px 大小的缩略图 (足够列表显示，性能开销极小)
                var thumb = await File.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.PicturesView, 100);
                if (thumb != null)
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        var bmp = new BitmapImage();
                        bmp.SetSource(thumb);
                        Thumbnail = bmp;
                    });
                }
            }
            catch
            {
                // 如果获取失败（如文件被占用），保持 null，界面将显示占位符
            }
        }
    }
}