using Windows.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace BlueSapphire.Models
{
    // ✅ 新增：原生的分组数据模型
    public class DuplicateGroup : ObservableCollection<DuplicateItem>
    {
        public string GroupTitle { get; set; }
        public DuplicateGroup(string title) { GroupTitle = title; }
    }

    public partial class DuplicateItem : ObservableObject
    {
        public StorageFile File { get; init; }
        public bool IsKeepSuggestion { get; init; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        private BitmapImage? _thumbnail;
        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        public string DisplayName => File.Name;
        public string DateString => File.DateCreated.ToString("g");
        public string PathString => File.Path;

        public Visibility SuggestionVisibility => IsKeepSuggestion ? Visibility.Visible : Visibility.Collapsed;

        public DuplicateItem(StorageFile file, bool isKeepSuggestion = false)
        {
            File = file;
            IsKeepSuggestion = isKeepSuggestion;
            // 删除属于不可逆的高风险选择。扫描结果默认全部不选，
            // 必须由用户逐项确认后才允许移入回收站。
            IsChecked = false;
        }

        public async Task LoadThumbnailAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            if (Thumbnail != null) return;
            try
            {
                var thumb = await File.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.PicturesView, 100);
                if (thumb != null)
                {
                    bool queued = dispatcherQueue.TryEnqueue(() =>
                    {
                        using (thumb)
                        {
                            var bmp = new BitmapImage();
                            bmp.SetSource(thumb);
                            Thumbnail = bmp;
                        }
                    });
                    if (!queued)
                    {
                        thumb.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // 缩略图属装饰性内容，加载失败只影响占位显示，不影响列表数据本身。
            }
        }
    }
}
