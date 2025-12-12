using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace BlueSapphire.Models
{
    // ==========================================
    // 1. 数据模型: ImageItem (主界面图片)
    // ==========================================
    public partial class ImageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string? FileName { get; set; }
        public string? ImagePath { get; set; }
        public DateTimeOffset DateCreated { get; set; }
        public ulong FileSize { get; set; }

        public string DateCreatedString => DateCreated.ToString("yyyy-MM-dd");
        public string FileSizeString => FormatBytes(FileSize);

        // 静态方法优化
        private static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private BitmapImage? _imageSource;
        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set { if (_imageSource != value) { _imageSource = value; OnPropertyChanged(); } }
        }

        private bool _isImageLoading = false;
        public bool IsImageLoading
        {
            get => _isImageLoading;
            set { if (_isImageLoading != value) { _isImageLoading = value; OnPropertyChanged(); } }
        }

        private bool _isLoaded = false;

        public async Task LoadImageAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath)) return;
            _isLoaded = true;
            IsImageLoading = true;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(ImagePath);
                // 200u 解决类型转换错误
                using var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 200u);
                if (thumb != null)
                {
                    var memoryStream = new InMemoryRandomAccessStream();
                    await RandomAccessStream.CopyAsync(thumb, memoryStream);
                    memoryStream.Seek(0);

                    dispatcherQueue.TryEnqueue(() =>
                    {
                        var bitmap = new BitmapImage();
                        bitmap.SetSource(memoryStream);
                        bitmap.DecodePixelWidth = 200;
                        ImageSource = bitmap;
                    });
                }
            }
            catch { _isLoaded = false; }
            finally { IsImageLoading = false; }
        }
    }
}