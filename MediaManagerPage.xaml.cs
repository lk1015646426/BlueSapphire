using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.Foundation;

namespace BlueSapphire
{
    // ==========================================
    // 1. 数据模型类: ImageItem
    // ==========================================
    public class ImageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string? FileName { get; set; }
        public string? ImagePath { get; set; }
        public DateTimeOffset DateCreated { get; set; }
        public ulong FileSize { get; set; }

        public string DateCreatedString => DateCreated.ToString("yyyy-MM-dd");
        // [新增] 格式化文件大小 (例如: 2.5 MB)
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
            set
            {
                if (_imageSource != value)
                {
                    _imageSource = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isLoaded = false;
        public async Task LoadImageAsync(DispatcherQueue dispatcherQueue)
        {
            if (_isLoaded || string.IsNullOrEmpty(ImagePath)) return;
            _isLoaded = true;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(this.ImagePath);
                using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var memoryStream = new InMemoryRandomAccessStream();
                    await RandomAccessStream.CopyAsync(fileStream, memoryStream);
                    memoryStream.Seek(0);

                    dispatcherQueue.TryEnqueue(() =>
                    {
                        var bitmap = new BitmapImage();
                        _ = bitmap.SetSourceAsync(memoryStream);
                        bitmap.DecodePixelWidth = 200;
                        this.ImageSource = bitmap;
                    });
                }
            }
            catch { _isLoaded = false; }
        }
    }

    // ==========================================
    // 2. 页面逻辑类: MediaManagerPage
    // ==========================================
    public sealed partial class MediaManagerPage : Page, ITool, INotifyPropertyChanged
    {
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private IncrementalLoadingCollection<ImageItem>? _images;
        public IncrementalLoadingCollection<ImageItem>? Images
        {
            get => _images;
            set
            {
                if (_images != value)
                {
                    _images = value;
                    OnPropertyChanged();
                }
            }
        }

        private StorageFolder? _currentFolder;
        private bool _isDialogActive = false;
        private string CurrentSortTag { get; set; } = "Name";
        public MediaManagerPage()
        {
            this.InitializeComponent();
            UpdateSortDirectionIcon();
        }

        public void Initialize() { }

        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is ImageItem item)
            {
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        // --- UI 状态控制 (视觉优化版) ---

        // [修改] 忙碌状态现在使用 Overlay，不隐藏图片网格
        private void SetBusyState(string statusText)
        {
            ScanningProgressBar.Visibility = Visibility.Collapsed;
            LoadingStatusText.Text = statusText;
            StatusOverlay.Visibility = Visibility.Visible; // 显示半透明遮罩
        }

        private void StartProgressState(string statusText, int totalItems)
        {
            ScanningProgressBar.Visibility = Visibility.Visible;
            ScanningProgressBar.IsIndeterminate = false;
            ScanningProgressBar.Minimum = 0;
            ScanningProgressBar.Maximum = totalItems;
            ScanningProgressBar.Value = 0;

            LoadingStatusText.Text = statusText;
            StatusOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateProgressValue(int currentValue)
        {
            ScanningProgressBar.Value = currentValue;
        }

        // [修改] 恢复时隐藏 Overlay
        private void RestoreUI(bool hasContent = true)
        {
            ScanningProgressBar.Visibility = Visibility.Collapsed;
            StatusOverlay.Visibility = Visibility.Collapsed; // 隐藏遮罩
            StatusBlock.Text = "STATUS: READY";
            if (hasContent)
            {
                ImageGrid.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ImageGrid.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }

        private async void ShowTipDialog(string message)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "提示",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        private void HandleError(string message)
        {
            RestoreUI(Images?.Any() == true);
            // 这里为了保证错误能弹窗，也尝试先解锁
            _isDialogActive = false;
            ShowTipDialog(message);
        }

        // --- 排序逻辑 ---

        private void UpdateSortDirectionIcon()
        {
            bool isDescending = SortDirectionToggle.IsChecked ?? false;
            if (!isDescending)
            {
                SortDirectionIcon.Glyph = "\uE74B";
                ToolTipService.SetToolTip(SortDirectionToggle, "当前: 升序");
            }
            else
            {
                SortDirectionIcon.Glyph = "\uE74A";
                ToolTipService.SetToolTip(SortDirectionToggle, "当前: 降序");
            }
        }

        private async void OnSortFieldMenuItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string tag)
            {
                CurrentSortTag = tag;
                SortFieldButton.Content = item.Text;
                if (_currentFolder != null) await LoadFolderContent(_currentFolder);
            }
        }

        private async void OnSortDirectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateSortDirectionIcon();
            if (_currentFolder != null) await LoadFolderContent(_currentFolder);
        }

        // --- 打开文件夹 ---

        public async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                var openPicker = new FolderPicker();
                openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                openPicker.FileTypeFilter.Add("*");

                if (MainWindow.Instance != null)
                {
                    WinRT.Interop.InitializeWithWindow.Initialize(openPicker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));
                }

                StorageFolder folder = await openPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    _currentFolder = folder;
                    await LoadFolderContent(folder);
                }
            }
            catch (Exception ex)
            {
                HandleError(ex.Message);
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        // --- 删除逻辑 ---

        private async void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (_isDialogActive) return;
            _isDialogActive = true;

            var selectedItems = ImageGrid.SelectedItems.Cast<ImageItem>().ToList();
            if (selectedItems.Count == 0)
            {
                _isDialogActive = false;
                ShowTipDialog("请先选择图片");
                return;
            }

            try
            {
                ContentDialog deleteDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要彻底删除这 {selectedItems.Count} 个文件吗？此操作不可恢复。",
                    PrimaryButtonText = "执行删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                if (await deleteDialog.ShowAsync() != ContentDialogResult.Primary) return;
                StartProgressState($"正在删除... (0/{selectedItems.Count})", selectedItems.Count);

                int deletedCount = 0;
                int processedCount = 0;
                foreach (var item in selectedItems)
                {
                    processedCount++;
                    UpdateProgressValue(processedCount);
                    LoadingStatusText.Text = $"正在删除... ({processedCount}/{selectedItems.Count})";

                    try
                    {
                        if (item.ImagePath != null)
                        {
                            var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                            await file.DeleteAsync(StorageDeleteOption.Default);
                            Images?.Remove(item);
                            deletedCount++;
                        }
                    }
                    catch { }
                }

                if (Images != null) CountBlock.Text = $"ITEMS: {Images.Count}";
                RestoreUI(true);

                // 【修复】手动释放锁，确保完成提示能弹出来
                _isDialogActive = false;
                ShowTipDialog($"操作完成。成功删除 {deletedCount} 个文件。");
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        // --- 去重逻辑 ---

        private async void OnScanDuplicatesClicked(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) { ShowTipDialog("请先导入文件夹"); return; }
            if (_isDialogActive) return;
            _isDialogActive = true;
            try
            {
                SetBusyState("正在扫描重复项...");
                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp" });
                queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, new[] { "System.Size" });
                var files = await _currentFolder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (files.Count < 2)
                {
                    RestoreUI(true);
                    // 【修复】手动释放锁
                    _isDialogActive = false;
                    ShowTipDialog("文件太少，无需扫描");
                    return;
                }

                var fileInfos = new List<(StorageFile f, ulong s)>();
                foreach (var f in files)
                {
                    var p = await f.GetBasicPropertiesAsync();
                    fileInfos.Add((f, p.Size));
                }

                var groups = fileInfos.GroupBy(x => x.s).Where(g => g.Count() > 1).ToList();
                // [修改点 1] 初筛：如果没有文件大小相同的，直接提示
                if (!groups.Any())
                {
                    RestoreUI(true);
                    // 【修复】手动释放锁，否则 "ShowTipDialog" 认为正忙而不显示
                    _isDialogActive = false;
                    ShowTipDialog("恭喜，当前文件夹没有发现重复文件！");
                    return;
                }
                // ------------------------------------------

                var dupes = new List<List<StorageFile>>();
                // --- MD5 计算过程 ---
                await Task.Run(async () =>
                {
                    foreach (var g in groups)
                    {
                        var hashes = new Dictionary<string, List<StorageFile>>();
                        foreach (var item in g)
                        {
                            var h = await FileHelper.ComputeMD5Async(item.f);
                            if (!string.IsNullOrEmpty(h))
                            {
                                if (!hashes.ContainsKey(h)) hashes[h] = new List<StorageFile>();
                                hashes[h].Add(item.f);
                            }
                        }
                        foreach (var hg in hashes.Values)
                        {
                            if (hg.Count > 1) dupes.Add(hg);
                        }
                    }
                });
                RestoreUI(true);

                // 【修复】释放锁，准备显示结果
                _isDialogActive = false;

                // [修改点 2] 复筛：如果计算完 MD5 发现内容其实不同，也提示
                if (dupes.Any())
                {
                    await ShowDuplicateResultsDialog(dupes);
                }
                else
                {
                    ShowTipDialog("恭喜，当前文件夹没有发现重复文件！");
                }
                // ------------------------------------------
            }
            catch (Exception ex)
            {
                HandleError(ex.Message);
            }
            finally
            {
                _isDialogActive = false;
            }
        }

        private async Task ShowDuplicateResultsDialog(List<List<StorageFile>> dupes)
        {
            var sp = new StackPanel { Spacing = 10, Padding = new Thickness(0, 0, 10, 0) };
            var toDel = new List<StorageFile>();
            int idx = 1;

            foreach (var g in dupes)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"重复组 #{idx++} ({g.Count}个)",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 255, 255))
                });
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var f = sorted[i];
                    bool keep = (i == 0);
                    var cb = new CheckBox
                    {
                        Content = $"{f.Name}",
                        IsChecked = !keep,
                        Tag = f,
                        Opacity = keep ? 0.5 : 1
                    };
                    if (keep)
                    {
                        cb.Content += " [建议保留]";
                        cb.Foreground = new SolidColorBrush(Colors.LightGreen);
                    }

                    cb.Checked += (s, e) => toDel.Add(f);
                    cb.Unchecked += (s, e) => toDel.Remove(f);
                    if (!keep) toDel.Add(f);
                    sp.Children.Add(cb);
                }
            }

            var scroll = new ScrollViewer { Content = sp, MaxHeight = 400 };
            var dlg = new ContentDialog
            {
                Title = $"发现 {dupes.Count} 组重复文件",
                Content = scroll,
                PrimaryButtonText = "删除选中项",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary && toDel.Any())
            {
                await PerformDeleteLoop(toDel);
            }
        }

        private async Task PerformDeleteLoop(List<StorageFile> files)
        {
            StartProgressState($"正在清理... (0/{files.Count})", files.Count);
            int c = 0;
            int processed = 0;

            foreach (var f in files)
            {
                processed++;
                UpdateProgressValue(processed);
                LoadingStatusText.Text = $"正在清理... ({processed}/{files.Count})";

                try
                {
                    await f.DeleteAsync(StorageDeleteOption.Default);
                    var i = Images?.FirstOrDefault(x => x.ImagePath == f.Path);
                    if (i != null) Images?.Remove(i);
                    c++;
                }
                catch { }
            }
            RestoreUI(true);

            // 确保清理完成后的弹窗也能显示
            _isDialogActive = false;
            ShowTipDialog($"成功清理 {c} 个重复文件");
        }

        // --- 加载逻辑 ---

        private async Task LoadFolderContent(StorageFolder folder)
        {
            if (folder == null) return;
            try
            {
                SetBusyState("正在索引元数据...");
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                // 注意：加载新文件夹时，为了避免混乱，暂时隐藏旧 Grid
                // 一旦数据准备好，RestoreUI 会立刻显示它
                ImageGrid.Visibility = Visibility.Collapsed;
                Images = null;
                PathBlock.Text = $"PATH: {folder.Path}";

                var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, new List<string> { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp" })
                {
                    FolderDepth = FolderDepth.Deep
                };
                var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

                if (files.Count == 0)
                {
                    CountBlock.Text = "ITEMS: 0";
                    RestoreUI(false);
                    return;
                }

                CountBlock.Text = $"ITEMS: {files.Count}";
                LoadingStatusText.Text = $"正在处理 {files.Count} 个文件...";

                var allItems = new List<ImageItem>();
                foreach (var file in files)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    allItems.Add(new ImageItem
                    {
                        FileName = file.Name,
                        ImagePath = file.Path,
                        DateCreated = file.DateCreated,
                        FileSize = props.Size
                    });
                }

                bool isDescending = SortDirectionToggle.IsChecked ?? false;
                IEnumerable<ImageItem> sortedEnumerable;

                switch (CurrentSortTag)
                {
                    case "Date":
                        sortedEnumerable = isDescending
                            ? allItems.OrderByDescending(x => x.DateCreated)
                            : allItems.OrderBy(x => x.DateCreated);
                        break;
                    case "Size":
                        sortedEnumerable = isDescending
                            ? allItems.OrderByDescending(x => x.FileSize)
                            : allItems.OrderBy(x => x.FileSize);
                        break;
                    case "Name":
                    default:
                        sortedEnumerable = isDescending
                            ? allItems.OrderByDescending(x => x.FileName)
                            : allItems.OrderBy(x => x.FileName);
                        break;
                }

                var sortedList = sortedEnumerable.ToList();
                Images = new IncrementalLoadingCollection<ImageItem>(async (token, count) =>
                {
                    return await Task.Run(() =>
                    {
                        var currentLen = Images?.Count ?? 0;
                        return sortedList.Skip(currentLen).Take((int)count);
                    });
                });
                RestoreUI(true);
            }
            catch (Exception ex)
            {
                HandleError($"加载失败: {ex.Message}");
            }
        }

        private void ImageGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ImageGrid.SelectedItem is ImageItem item)
            {
                ShowTipDialog($"已选择: {item.FileName}");
            }
        }
    }
}