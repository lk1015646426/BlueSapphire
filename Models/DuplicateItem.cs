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
            IsChecked = !isKeepSuggestion;
        }

        public async Task LoadThumbnailAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            if (Thumbnail != null) return;
            try
            {
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
            catch { }
        }
    }
}