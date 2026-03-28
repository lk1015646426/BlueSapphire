using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Services;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, IMediaViewInteraction
    {
        private readonly HashSet<UIElement> _microInteractionElements = new();

        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            InitializeComponent();

            ViewModel = App.Current.Services.GetRequiredService<MediaManagerViewModel>();
            ViewModel.Initialize(this, DispatcherQueue);

            Loaded += MediaManagerPage_Loaded;
            Unloaded += MediaManagerPage_Unloaded;
        }

        public async Task<StorageFolder?> PickFolderAsync()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            folderPicker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, App.MainWindowHandle);
            return await folderPicker.PickSingleFolderAsync();
        }

        public async Task<StorageFile?> PickImageFileAsync()
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".bmp");
            filePicker.FileTypeFilter.Add(".webp");

            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, App.MainWindowHandle);
            return await filePicker.PickSingleFileAsync();
        }

        public async Task<StorageFile?> PickCsvFileAsync()
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            filePicker.FileTypeFilter.Add(".csv");

            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, App.MainWindowHandle);
            return await filePicker.PickSingleFileAsync();
        }

        public async Task<StorageFile?> PickLyricsFileAsync()
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            filePicker.FileTypeFilter.Add(".lrc");
            filePicker.FileTypeFilter.Add(".txt");

            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, App.MainWindowHandle);
            return await filePicker.PickSingleFileAsync();
        }

        public async Task<StorageFile?> PickPlaylistFileAsync()
        {
            var filePicker = new Windows.Storage.Pickers.FileOpenPicker();
            filePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            filePicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            filePicker.FileTypeFilter.Add(".m3u8");
            filePicker.FileTypeFilter.Add(".m3u");

            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, App.MainWindowHandle);
            return await filePicker.PickSingleFileAsync();
        }

        public Task SelectItemsByPathsAsync(IReadOnlyCollection<string> paths)
        {
            return RunOnUiThreadAsync(async () =>
            {
                var lookup = new HashSet<string>(
                    paths?.Where(path => !string.IsNullOrWhiteSpace(path)) ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                ImageGrid.SelectedItems.Clear();
                if (lookup.Count == 0)
                {
                    await ViewModel.UpdateAudioPreviewSelectionAsync(ImageGrid.SelectedItems);
                    return;
                }

                while (ViewModel.Images != null)
                {
                    var loadedPaths = ViewModel.Images
                        .Where(item => !string.IsNullOrWhiteSpace(item.ImagePath))
                        .Select(item => item.ImagePath!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (!ViewModel.Images.HasMoreItems || lookup.All(path => loadedPaths.Contains(path)))
                    {
                        break;
                    }

                    await ViewModel.Images.LoadMoreItemsAsync(100);
                }

                ImageItem? firstItem = null;
                foreach (var item in ViewModel.Images ?? Enumerable.Empty<ImageItem>())
                {
                    if (!string.IsNullOrWhiteSpace(item.ImagePath) && lookup.Contains(item.ImagePath))
                    {
                        ImageGrid.SelectedItems.Add(item);
                        firstItem ??= item;
                    }
                }

                if (firstItem != null)
                {
                    ImageGrid.ScrollIntoView(firstItem);
                }

                await ViewModel.UpdateAudioPreviewSelectionAsync(ImageGrid.SelectedItems);
            });
        }

        public async Task<bool> ShowRenamePreviewAsync(List<RenamePreviewItem> items, int skippedCount)
        {
            var dialog = new RenamePreviewDialog(items, skippedCount)
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public async Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> duplicates)
        {
            var dialog = new DuplicateResultDialog(duplicates, DispatcherQueue)
            {
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                return dialog.GetSelectedFiles();
            }

            return new List<StorageFile>();
        }

        public async Task ShowDocumentConversionResultsAsync(DocumentConversionBatchReport report)
        {
            var dialog = new DocumentConversionResultDialog(report)
            {
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        public async Task<DocumentConversionBatchReport?> ShowDocumentTaskHistoryAsync(IReadOnlyList<DocumentConversionBatchReport> reports)
        {
            var dialog = new DocumentTaskHistoryDialog(reports)
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.SelectedReport : null;
        }

        public async Task<AudioTrimRequest?> ShowAudioTrimDialogAsync(string fileName, TimeSpan? duration, bool isBatch = false)
        {
            var dialog = new AudioTrimDialog(fileName, duration, isBatch)
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.Request : null;
        }

        public async Task<AudioTagEditRequest?> ShowAudioTagEditDialogAsync(AudioTagEditSeed seed)
        {
            var dialog = new AudioTagEditDialog(seed)
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.Request : null;
        }

        public async Task ShowTipAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        public async Task<bool> ShowDeleteConfirmationAsync(int count)
        {
            var dialog = new ContentDialog
            {
                Title = "删除确认",
                Content = $"确定要将这 {count} 个文件移至回收站吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                (args.Item as ImageItem)?.CancelLoad();
                return;
            }

            if (args.Item is ImageItem item)
            {
                _ = item.LoadImageAsync(DispatcherQueue);
            }
        }

        private async void ImageGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is ImageItem item)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                    await Windows.System.Launcher.LaunchFileAsync(file);
                }
                catch
                {
                }
            }
        }

        private async void ImageGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await ViewModel.UpdateAudioPreviewSelectionAsync(ImageGrid.SelectedItems);
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.DeleteSelectedCommand.Execute(ImageGrid.SelectedItems);
        }

        private async void OnDocumentFormatConvertClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string targetKey)
            {
                await ViewModel.ConvertSelectedDocumentsToTargetAsync(ImageGrid.SelectedItems, targetKey);
            }
        }

        private async void OnImageFormatConvertClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string targetKey)
            {
                await ViewModel.ConvertSelectedImagesToTargetAsync(ImageGrid.SelectedItems, targetKey);
            }
        }

        private async void OnImageResizeClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string presetKey)
            {
                await ViewModel.ResizeSelectedImagesAsync(ImageGrid.SelectedItems, presetKey);
            }
        }

        private async void OnImageCropClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string presetKey)
            {
                await ViewModel.CropSelectedImagesAsync(ImageGrid.SelectedItems, presetKey);
            }
        }

        private async void OnImageCompressClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string presetKey)
            {
                await ViewModel.CompressSelectedImagesAsync(ImageGrid.SelectedItems, presetKey);
            }
        }

        private async void OnImageEnhanceClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string presetKey)
            {
                await ViewModel.EnhanceSelectedImagesAsync(ImageGrid.SelectedItems, presetKey);
            }
        }

        private async void OnAudioFormatConvertClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string targetKey)
            {
                await ViewModel.ConvertSelectedAudioToTargetAsync(ImageGrid.SelectedItems, targetKey);
            }
        }

        private async void OnAudioMetadataRenameClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string patternKey)
            {
                await ViewModel.RenameSelectedAudioByMetadataAsync(ImageGrid.SelectedItems, patternKey);
            }
        }

        private async void OnAudioFilenameImportClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string patternKey)
            {
                await ViewModel.ImportSelectedAudioTagsFromFileNameAsync(ImageGrid.SelectedItems, patternKey);
            }
        }

        private async void OnAudioCoverApplyClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ApplySelectedAudioCoverArtCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private async void OnAudioCoverImportSidecarClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ImportSelectedAudioCoverArtFromSidecarCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private async void OnAudioCoverClearClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ClearSelectedAudioCoverArtCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private async void OnAudioCoverExportClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ExportSelectedAudioCoverArtCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private async void OnAudioLyricsImportClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ImportSelectedAudioLyricsFromFileCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private async void OnAudioLyricsImportSidecarClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ImportSelectedAudioLyricsFromSidecarCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private async void OnAudioLyricsExportClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ExportSelectedAudioLyricsCommand.ExecuteAsync(ImageGrid.SelectedItems);
        }

        private void AudioPreviewSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.BeginAudioPreviewSeek();
        }

        private void AudioPreviewSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.CommitAudioPreviewSeek(AudioPreviewSlider.Value);
        }

        private void AudioPreviewSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.CommitAudioPreviewSeek(AudioPreviewSlider.Value);
        }

        public async Task<string?> ShowInputPromptAsync(string title, string message, string defaultText)
        {
            var textBox = new TextBox { Text = defaultText, AcceptsReturn = false };
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, textBox }
                },
                PrimaryButtonText = "确定",
                CloseButtonText = "跳过",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? textBox.Text : null;
        }

        private void Tab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Border clickedTab && clickedTab.Tag is string mediaType)
            {
                ViewModel.ChangeMediaTypeCommand.Execute(mediaType);
                ResetAllTabsVisuals();
                clickedTab.Opacity = 1.0;

                var textBlock = (TextBlock)((StackPanel)clickedTab.Child).Children[1];
                var icon = (FontIcon)((StackPanel)clickedTab.Child).Children[0];

                var brush = mediaType switch
                {
                    "Image" => (Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["ColorImage"],
                    "Audio" => (Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["ColorAudio"],
                    "Doc" => (Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["ColorDoc"],
                    _ => (Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["ColorAll"]
                };

                clickedTab.BorderBrush = brush;
                textBlock.Foreground = brush;
                icon.Foreground = brush;

                _ = ViewModel.UpdateAudioPreviewSelectionAsync(ImageGrid.SelectedItems);
            }
        }

        private void ResetAllTabsVisuals()
        {
            var defaultBrush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Resources["TextMain"];

            Border[] tabs = { TabAll, TabImage, TabAudio, TabDoc };
            foreach (var tab in tabs)
            {
                tab.Opacity = 0.5;
                tab.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                var stack = (StackPanel)tab.Child;
                ((FontIcon)stack.Children[0]).Foreground = defaultBrush;
                ((TextBlock)stack.Children[1]).Foreground = defaultBrush;
            }
        }

        public Microsoft.UI.Xaml.Media.SolidColorBrush GetThemeBrush(string mediaType)
        {
            var key = mediaType switch
            {
                "Image" => "ColorImage",
                "Audio" => "ColorAudio",
                "Doc" => "ColorDoc",
                _ => "ColorAll"
            };
            return (Microsoft.UI.Xaml.Media.SolidColorBrush)Resources[key];
        }

        public string GetSortButtonText(string sortField)
        {
            var name = sortField switch
            {
                "Date" => "日期",
                "Size" => "大小",
                _ => "名称"
            };
            return $"排序: {name}";
        }

        private void MediaManagerPage_Loaded(object sender, RoutedEventArgs e)
        {
            HookMicroInteractions(ImportSourceButton);
            HookMicroInteractions(ActionToolbar);
            HookMicroInteractions(AudioPreviewCommandBar);

            Border[] tabs = { TabAll, TabImage, TabAudio, TabDoc };
            foreach (var tab in tabs)
            {
                HookMicroInteractions(tab);
            }
        }

        private void HookMicroInteractions(DependencyObject? root)
        {
            if (root == null)
            {
                return;
            }

            if (root is UIElement element && (root is Button || root is DropDownButton || (root is Border border && border.Tag is string)))
            {
                AttachMicroInteraction(element);
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                HookMicroInteractions(VisualTreeHelper.GetChild(root, i));
            }
        }

        private void AttachMicroInteraction(UIElement element)
        {
            if (!_microInteractionElements.Add(element))
            {
                return;
            }

            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.Loaded += InteractiveElement_Loaded;
                frameworkElement.SizeChanged += InteractiveElement_SizeChanged;
            }

            element.PointerEntered += InteractiveElement_PointerEntered;
            element.PointerExited += InteractiveElement_PointerExited;
            element.PointerPressed += InteractiveElement_PointerPressed;
            element.PointerReleased += InteractiveElement_PointerReleased;
            element.PointerCanceled += InteractiveElement_PointerExited;
            UpdateInteractiveCenterPoint(element);
        }

        private static void InteractiveElement_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                UpdateInteractiveCenterPoint(element);
            }
        }

        private static void InteractiveElement_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is UIElement element)
            {
                UpdateInteractiveCenterPoint(element);
            }
        }

        private static void InteractiveElement_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                AnimateInteractiveScale(element, 1.03f, 140);
            }
        }

        private static void InteractiveElement_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                AnimateInteractiveScale(element, 1.0f, 160);
            }
        }

        private static void InteractiveElement_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                AnimateInteractiveScale(element, 0.98f, 80);
            }
        }

        private static void InteractiveElement_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                AnimateInteractiveScale(element, 1.03f, 120);
            }
        }

        private static void UpdateInteractiveCenterPoint(UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new Vector3(
                (float)Math.Max(0, element.RenderSize.Width / 2),
                (float)Math.Max(0, element.RenderSize.Height / 2),
                0f);
        }

        private static void AnimateInteractiveScale(UIElement element, float scale, int durationMilliseconds)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(1f, new Vector3(scale, scale, 1f));
            animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
            visual.StartAnimation("Scale", animation);
        }

        private Task RunOnUiThreadAsync(Func<Task> action)
        {
            var tcs = new TaskCompletionSource<object?>();
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        private void MediaManagerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MediaManagerPage_Loaded;
            Unloaded -= MediaManagerPage_Unloaded;
            ViewModel.ReleaseAudioPreview();
        }
    }
}


