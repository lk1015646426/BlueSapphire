using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading; // [新增]
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace BlueSapphire.Models
{
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
        private CancellationTokenSource? _loadingCts; // [新增] 用于取消加载任务

        public async Task LoadImageAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath)) return;

            // 取消之前的任何尝试
            _loadingCts?.Cancel();
            _loadingCts = new CancellationTokenSource();
            var token = _loadingCts.Token;

            _isLoaded = true;
            IsImageLoading = true;

            try
            {
                // [优化] 检查取消
                if (token.IsCancellationRequested) return;

                var file = await StorageFile.GetFileFromPathAsync(ImagePath);
                if (token.IsCancellationRequested) return;

                using var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 200u);
                if (token.IsCancellationRequested) return;

                if (thumb != null)
                {
                    var memoryStream = new InMemoryRandomAccessStream();
                    await RandomAccessStream.CopyAsync(thumb, memoryStream);
                    memoryStream.Seek(0);

                    if (token.IsCancellationRequested) return;

                    dispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        var bitmap = new BitmapImage();
                        bitmap.SetSource(memoryStream);
                        bitmap.DecodePixelWidth = 200;
                        ImageSource = bitmap;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                _isLoaded = false;
            }
            catch
            {
                _isLoaded = false;
            }
            finally
            {
                IsImageLoading = false;
            }
        }

        // [新增] 外部调用此方法取消加载（如滚动出屏幕时）
        public void CancelLoad()
        {
            _loadingCts?.Cancel();
            _loadingCts = null;
            _isLoaded = false; // 重置状态，以便下次进入屏幕时重新尝试
            IsImageLoading = false;
        }
    }
}