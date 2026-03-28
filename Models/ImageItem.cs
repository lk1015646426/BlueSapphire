using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading; // [新增]
using System.Threading.Tasks;
using BlueSapphire.Helpers;
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
                    OnPropertyChanged(nameof(PreviewGlyph));
                    OnPropertyChanged(nameof(IsImageFile));
                    OnPropertyChanged(nameof(IsAudioFile));
                    OnPropertyChanged(nameof(IsDocumentFile));
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

        public string DateCreatedString => DateCreated.ToString("yyyy-MM-dd");
        public string FileSizeString => FormatBytes(FileSize);
        public string MetadataPrimaryText => BuildMetadataPrimaryText();
        public string MetadataSecondaryText => BuildMetadataSecondaryText();
        public string DetailLineText => BuildDetailLineText();
        public bool HasDetailLine => !string.IsNullOrWhiteSpace(DetailLineText);
        public bool HasAudioLyrics => !string.IsNullOrWhiteSpace(AudioLyrics);
        public bool HasAudioAssetBadges => HasEmbeddedCoverArt || HasAudioLyrics;
        public bool HasCustomTags => _customTags.Count > 0;
        public string CustomTagSummaryText => BuildCustomTagSummaryText();
        public string PreviewGlyph => GetPreviewGlyph();
        public bool IsImageFile => MediaFileCatalog.IsImage(FileName);
        public bool IsAudioFile => MediaFileCatalog.IsAudio(FileName);
        public bool IsDocumentFile => MediaFileCatalog.IsDocument(FileName);
        public string MediaTypeLabel => GetMediaTypeLabel();
        public string FileExtensionLabel => GetFileExtensionLabel();

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

        private TimeSpan _audioDuration;
        public TimeSpan AudioDuration
        {
            get => _audioDuration;
            set
            {
                if (_audioDuration != value)
                {
                    _audioDuration = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AudioDurationString));
                    OnPropertyChanged(nameof(MetadataPrimaryText));
                }
            }
        }

        private string? _audioArtist;
        public string? AudioArtist
        {
            get => _audioArtist;
            set
            {
                if (_audioArtist != value)
                {
                    _audioArtist = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DetailLineText));
                    OnPropertyChanged(nameof(HasDetailLine));
                }
            }
        }

        private string? _audioAlbum;
        public string? AudioAlbum
        {
            get => _audioAlbum;
            set
            {
                if (_audioAlbum != value)
                {
                    _audioAlbum = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DetailLineText));
                    OnPropertyChanged(nameof(HasDetailLine));
                }
            }
        }

        private string? _audioAlbumArtist;
        public string? AudioAlbumArtist
        {
            get => _audioAlbumArtist;
            set
            {
                if (_audioAlbumArtist != value)
                {
                    _audioAlbumArtist = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DetailLineText));
                    OnPropertyChanged(nameof(HasDetailLine));
                }
            }
        }

        private string? _audioTitle;
        public string? AudioTitle
        {
            get => _audioTitle;
            set
            {
                if (_audioTitle != value)
                {
                    _audioTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _audioComposer;
        public string? AudioComposer
        {
            get => _audioComposer;
            set
            {
                if (_audioComposer != value)
                {
                    _audioComposer = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _audioGenre;
        public string? AudioGenre
        {
            get => _audioGenre;
            set
            {
                if (_audioGenre != value)
                {
                    _audioGenre = value;
                    OnPropertyChanged();
                }
            }
        }

        private uint _audioTrackNumber;
        public uint AudioTrackNumber
        {
            get => _audioTrackNumber;
            set
            {
                if (_audioTrackNumber != value)
                {
                    _audioTrackNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        private uint _audioDiscNumber;
        public uint AudioDiscNumber
        {
            get => _audioDiscNumber;
            set
            {
                if (_audioDiscNumber != value)
                {
                    _audioDiscNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        private uint _audioYear;
        public uint AudioYear
        {
            get => _audioYear;
            set
            {
                if (_audioYear != value)
                {
                    _audioYear = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _audioComment;
        public string? AudioComment
        {
            get => _audioComment;
            set
            {
                if (_audioComment != value)
                {
                    _audioComment = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _audioLyrics;
        public string? AudioLyrics
        {
            get => _audioLyrics;
            set
            {
                if (_audioLyrics != value)
                {
                    _audioLyrics = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasAudioLyrics));
                    OnPropertyChanged(nameof(HasAudioAssetBadges));
                }
            }
        }

        private uint _audioBitrate;
        public uint AudioBitrate
        {
            get => _audioBitrate;
            set
            {
                if (_audioBitrate != value)
                {
                    _audioBitrate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AudioBitrateString));
                    OnPropertyChanged(nameof(MetadataSecondaryText));
                }
            }
        }

        public string AudioDurationString => FormatDuration(AudioDuration);

        public string ImageResolutionText => ImageWidth > 0 && ImageHeight > 0
            ? $"{ImageWidth}x{ImageHeight}"
            : string.Empty;

        public string ImageBitDepthText => ImageBitDepth.HasValue && ImageBitDepth.Value > 0
            ? $"{ImageBitDepth.Value}-bit"
            : string.Empty;

        public string ImageDateTakenText => ImageDateTaken.HasValue
            ? $"拍摄于 {ImageDateTaken.Value:yyyy-MM-dd HH:mm}"
            : string.Empty;

        public string AudioBitrateString => AudioBitrate == 0
            ? string.Empty
            : $"{Math.Max(1, (int)Math.Round(AudioBitrate / 1000d, MidpointRounding.AwayFromZero))} kbps";

        private uint _audioSampleRate;
        public uint AudioSampleRate
        {
            get => _audioSampleRate;
            set
            {
                if (_audioSampleRate != value)
                {
                    _audioSampleRate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AudioSampleRateString));
                    OnPropertyChanged(nameof(MetadataSecondaryText));
                }
            }
        }

        public string AudioSampleRateString => AudioSampleRate == 0
            ? string.Empty
            : $"{AudioSampleRate / 1000d:0.#} kHz";

        private bool _hasEmbeddedCoverArt;
        public bool HasEmbeddedCoverArt
        {
            get => _hasEmbeddedCoverArt;
            set
            {
                if (_hasEmbeddedCoverArt != value)
                {
                    _hasEmbeddedCoverArt = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasAudioAssetBadges));
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
                var thumb = await file.GetThumbnailAsync(GetThumbnailMode(), 200, ThumbnailOptions.UseCurrentScale);

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

        public void InvalidatePreview()
        {
            CancelLoad();
            ImageSource = null;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return string.Empty;
            }

            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss")
                : duration.ToString(@"mm\:ss");
        }

        private string BuildMetadataPrimaryText()
        {
            if (MediaFileCatalog.IsAudio(FileName))
            {
                return AudioDuration > TimeSpan.Zero ? AudioDurationString : DateCreatedString;
            }

            if (MediaFileCatalog.IsImage(FileName))
            {
                return !string.IsNullOrWhiteSpace(ImageResolutionText) ? ImageResolutionText : DateCreatedString;
            }

            return DateCreatedString;
        }

        private string BuildMetadataSecondaryText()
        {
            if (MediaFileCatalog.IsImage(FileName))
            {
                var imageParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ImageFormat))
                {
                    imageParts.Add(ImageFormat!);
                }

                if (!string.IsNullOrWhiteSpace(ImageBitDepthText))
                {
                    imageParts.Add(ImageBitDepthText);
                }

                return imageParts.Count > 0
                    ? string.Join(" · ", imageParts)
                    : FileSizeString;
            }

            var parts = new List<string>();

            if (AudioBitrate > 0)
            {
                parts.Add(AudioBitrateString);
            }

            if (AudioSampleRate > 0)
            {
                parts.Add(AudioSampleRateString);
            }

            return parts.Count > 0
                ? string.Join(" · ", parts)
                : FileSizeString;
        }

        private string BuildDetailLineText()
        {
            if (MediaFileCatalog.IsImage(FileName))
            {
                return !string.IsNullOrWhiteSpace(ImageDateTakenText)
                    ? ImageDateTakenText
                    : string.Empty;
            }

            string artist = AudioArtist?.Trim() ?? AudioAlbumArtist?.Trim() ?? string.Empty;
            string album = AudioAlbum?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
            {
                return $"{artist} · {album}";
            }

            if (!string.IsNullOrWhiteSpace(artist))
            {
                return artist;
            }

            if (!string.IsNullOrWhiteSpace(album))
            {
                return album;
            }

            if (!string.IsNullOrWhiteSpace(AudioGenre))
            {
                return AudioGenre.Trim();
            }

            return AudioComposer?.Trim() ?? string.Empty;
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

        private ThumbnailMode GetThumbnailMode()
        {
            if (MediaFileCatalog.IsAudio(FileName))
            {
                return ThumbnailMode.MusicView;
            }

            if (MediaFileCatalog.IsDocument(FileName))
            {
                return ThumbnailMode.DocumentsView;
            }

            return ThumbnailMode.PicturesView;
        }

        private string GetPreviewGlyph()
        {
            if (MediaFileCatalog.IsImage(FileName))
            {
                return "\uE8B9";
            }

            if (MediaFileCatalog.IsAudio(FileName))
            {
                return "\uE8D6";
            }

            if (MediaFileCatalog.IsDocument(FileName))
            {
                return "\uE8A5";
            }

            return "\uE81E";
        }

        private string GetMediaTypeLabel()
        {
            if (MediaFileCatalog.IsImage(FileName))
            {
                return "图片";
            }

            if (MediaFileCatalog.IsAudio(FileName))
            {
                return "音频";
            }

            if (MediaFileCatalog.IsDocument(FileName))
            {
                return "文档";
            }

            return "文件";
        }

        private string GetFileExtensionLabel()
        {
            string extension = System.IO.Path.GetExtension(FileName ?? string.Empty);
            return string.IsNullOrWhiteSpace(extension)
                ? "FILE"
                : extension.TrimStart('.').ToUpperInvariant();
        }
    }
}
