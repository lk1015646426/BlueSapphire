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

            // 取消之前的尝试
            _loadingCts?.Cancel();
            _loadingCts?.Dispose(); // ✅ 新增：彻底释放底层句柄
            _loadingCts = new CancellationTokenSource();
            var token = _loadingCts.Token;

            _isLoaded = true;
            IsImageLoading = true;

            try
            {
                // ✅ 绝杀优化：防抖！等待 100 毫秒。如果用户只是快速滚过，直接在这一步被掐断，绝对不碰硬盘！
                await Task.Delay(100, token);
                if (token.IsCancellationRequested) return;

                var file = await StorageFile.GetFileFromPathAsync(ImagePath);
                if (token.IsCancellationRequested) return;

                // 🚨 修复：去掉 using！我们要把流的控制权交给 UI 线程，不能在这里提前销毁
                var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 200, ThumbnailOptions.UseCurrentScale);

                if (token.IsCancellationRequested)
                {
                    thumb?.Dispose(); // 如果中途取消，手动销毁
                    return;
                }

                if (thumb != null)
                {
                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            thumb.Dispose();
                            return;
                        }

                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.DecodePixelWidth = 200;
                            await bitmap.SetSourceAsync(thumb); // 安全读取
                            ImageSource = bitmap;
                        }
                        catch { /* 忽略渲染异常 */ }
                        finally
                        {
                            // ✅ 修复：UI 线程把图片成功“吃”进内存后，再由 UI 线程负责销毁流
                            thumb.Dispose();
                        }
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // Task.Delay 被取消会抛出这个异常
                _isLoaded = false;
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
                // 确保在 UI 线程更新 Loading 状态
                dispatcherQueue.TryEnqueue(() => IsImageLoading = false);
            }
        }

        // [新增] 外部调用此方法取消加载（如滚动出屏幕时）
        public void CancelLoad()
        {
            _loadingCts?.Cancel();
            _loadingCts?.Dispose(); // ✅ 新增：彻底释放底层句柄
            _loadingCts = null;
            _isLoaded = false; // 重置状态，以便下次进入屏幕时重新尝试
            IsImageLoading = false;
        }
    }
}