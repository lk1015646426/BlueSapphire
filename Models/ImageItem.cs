using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BlueSapphire.Models
{
    public partial class ImageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string? _fileName;
        public string? FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsImageFile));
                    OnPropertyChanged(nameof(MediaTypeLabel));
                    OnPropertyChanged(nameof(FileExtensionLabel));
                    OnPropertyChanged(nameof(MetadataPrimaryText));
                    OnPropertyChanged(nameof(MetadataSecondaryText));
                    OnPropertyChanged(nameof(DetailLineText));
                    OnPropertyChanged(nameof(HasDetailLine));
                }
            }
        }

        private string? _imagePath;
        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                if (_imagePath != value)
                {
                    _imagePath = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTimeOffset _dateCreated;
        public DateTimeOffset DateCreated
        {
            get => _dateCreated;
            set
            {
                if (_dateCreated != value)
                {
                    _dateCreated = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DateCreatedString));
                    OnPropertyChanged(nameof(MetadataPrimaryText));
                }
            }
        }

        private ulong _fileSize;
        public ulong FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileSizeString));
                    OnPropertyChanged(nameof(MetadataSecondaryText));
                }
            }
        }

        private BitmapImage? _imageSource;
        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set
            {
                if (_imageSource != value)
                {
                    _imageSource = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isImageLoading;
        public bool IsImageLoading
        {
            get => _isImageLoading;
            set
            {
                if (_isImageLoading != value)
                {
                    _isImageLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        private uint _imageWidth;
        public uint ImageWidth
        {
            get => _imageWidth;
            set
            {
                if (_imageWidth != value)
                {
                    _imageWidth = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ImageResolutionText));
                    OnPropertyChanged(nameof(MetadataPrimaryText));
                }
            }
        }

        private uint _imageHeight;
        public uint ImageHeight
        {
            get => _imageHeight;
            set
            {
                if (_imageHeight != value)
                {
                    _imageHeight = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ImageResolutionText));
                    OnPropertyChanged(nameof(MetadataPrimaryText));
                }
            }
        }

        private string? _imageFormat;
        public string? ImageFormat
        {
            get => _imageFormat;
            set
            {
                if (_imageFormat != value)
                {
                    _imageFormat = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MetadataSecondaryText));
                }
            }
        }

        private ushort? _imageBitDepth;
        public ushort? ImageBitDepth
        {
            get => _imageBitDepth;
            set
            {
                if (_imageBitDepth != value)
                {
                    _imageBitDepth = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ImageBitDepthText));
                    OnPropertyChanged(nameof(MetadataSecondaryText));
                }
            }
        }

        private DateTimeOffset? _imageDateTaken;
        public DateTimeOffset? ImageDateTaken
        {
            get => _imageDateTaken;
            set
            {
                if (_imageDateTaken != value)
                {
                    _imageDateTaken = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ImageDateTakenText));
                    OnPropertyChanged(nameof(DetailLineText));
                    OnPropertyChanged(nameof(HasDetailLine));
                }
            }
        }

        private IReadOnlyList<string> _customTags = Array.Empty<string>();
        public IReadOnlyList<string> CustomTags
        {
            get => _customTags;
            set
            {
                var normalized = value?
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>();

                if (_customTags.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }

                _customTags = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomTags));
                OnPropertyChanged(nameof(CustomTagSummaryText));
            }
        }

        private bool _isLoaded;
        private CancellationTokenSource? _loadingCts;

        public string DateCreatedString => DateCreated.ToString("yyyy-MM-dd");
        public string FileSizeString => FormatBytes(FileSize);
        public string MetadataPrimaryText => !string.IsNullOrWhiteSpace(ImageResolutionText) ? ImageResolutionText : DateCreatedString;
        public string MetadataSecondaryText => BuildImageMetadataSecondaryText();
        public string DetailLineText => !string.IsNullOrWhiteSpace(ImageDateTakenText) ? ImageDateTakenText : string.Empty;
        public bool HasDetailLine => !string.IsNullOrWhiteSpace(DetailLineText);
        public bool HasCustomTags => _customTags.Count > 0;
        public string CustomTagSummaryText => BuildCustomTagSummaryText();
        public bool IsImageFile => MediaFileCatalog.IsImage(FileName);
        public string MediaTypeLabel => "图片";
        public string FileExtensionLabel => GetFileExtensionLabel();
        public string ImageResolutionText => ImageWidth > 0 && ImageHeight > 0 ? $"{ImageWidth}x{ImageHeight}" : string.Empty;
        public string ImageBitDepthText => ImageBitDepth.HasValue && ImageBitDepth.Value > 0 ? $"{ImageBitDepth.Value}-bit" : string.Empty;
        public string ImageDateTakenText => ImageDateTaken.HasValue ? $"拍摄于 {ImageDateTaken.Value:yyyy-MM-dd HH:mm}" : string.Empty;

        public async Task LoadImageAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath))
            {
                return;
            }

            _loadingCts?.Cancel();
            _loadingCts?.Dispose();
            var loadingCts = new CancellationTokenSource();
            _loadingCts = loadingCts;
            var token = loadingCts.Token;

            _isLoaded = true;
            IsImageLoading = true;
            bool handedOffToUi = false;

            try
            {
                await Task.Delay(100, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var file = await StorageFile.GetFileFromPathAsync(ImagePath);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 220, ThumbnailOptions.UseCurrentScale);
                if (token.IsCancellationRequested)
                {
                    thumb?.Dispose();
                    return;
                }

                if (thumb != null)
                {
                    handedOffToUi = dispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            var bitmap = new BitmapImage
                            {
                                DecodePixelWidth = 220
                            };
                            await bitmap.SetSourceAsync(thumb);
                            ImageSource = bitmap;
                        }
                        catch
                        {
                            _isLoaded = false;
                        }
                        finally
                        {
                            thumb.Dispose();
                            IsImageLoading = false;
                            if (ReferenceEquals(_loadingCts, loadingCts))
                            {
                                loadingCts.Dispose();
                                _loadingCts = null;
                            }
                        }
                    });
                    if (!handedOffToUi)
                    {
                        thumb.Dispose();
                        _isLoaded = false;
                    }
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
                if (!handedOffToUi)
                {
                    dispatcherQueue.TryEnqueue(() => IsImageLoading = false);
                }
                if (!handedOffToUi && ReferenceEquals(_loadingCts, loadingCts))
                {
                    loadingCts.Dispose();
                    _loadingCts = null;
                }
            }
        }

        public void CancelLoad()
        {
            _loadingCts?.Cancel();
            _loadingCts?.Dispose();
            _loadingCts = null;
            _isLoaded = false;
            IsImageLoading = false;
        }

        public void InvalidatePreview()
        {
            CancelLoad();
            ImageSource = null;
        }

        private string BuildImageMetadataSecondaryText()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ImageFormat))
            {
                parts.Add(ImageFormat!);
            }

            if (!string.IsNullOrWhiteSpace(ImageBitDepthText))
            {
                parts.Add(ImageBitDepthText);
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : FileSizeString;
        }

        private string BuildCustomTagSummaryText()
        {
            if (_customTags.Count == 0)
            {
                return string.Empty;
            }

            const int maxPreviewCount = 3;
            var previewTags = _customTags
                .Take(maxPreviewCount)
                .Select(tag => "#" + tag)
                .ToList();

            if (_customTags.Count > maxPreviewCount)
            {
                previewTags.Add($"+{_customTags.Count - maxPreviewCount}");
            }

            return string.Join(" · ", previewTags);
        }

        private string GetFileExtensionLabel()
        {
            string extension = System.IO.Path.GetExtension(FileName ?? string.Empty);
            return string.IsNullOrWhiteSpace(extension)
                ? "FILE"
                : extension.TrimStart('.').ToUpperInvariant();
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }
}
