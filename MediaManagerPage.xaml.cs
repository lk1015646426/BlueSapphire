using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, IMediaViewInteraction
    {
        private IReadOnlyList<Button> _featureCards = Array.Empty<Button>();
        private IReadOnlyList<(FrameworkElement Section, IReadOnlyList<Button> Cards)> _featureSections =
            Array.Empty<(FrameworkElement, IReadOnlyList<Button>)>();

        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<MediaManagerViewModel>();
            InitializeComponent();
            ViewModel.Initialize(this, DispatcherQueue);
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateWorkspaceLayout();
        }

        public async Task<StorageFolder?> PickFolderAsync()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, App.MainWindowHandle);
            return await folderPicker.PickSingleFolderAsync();
        }

        public async Task SelectItemsByPathsAsync(IReadOnlyCollection<string> paths)
        {
            if (paths.Count == 0)
            {
                return;
            }

            var lookup = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            ImageGrid.SelectedItems.Clear();

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
            return result == ContentDialogResult.Primary
                ? dialog.GetSelectedFiles()
                : new List<StorageFile>();
        }

        public async Task ShowTipAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        public async Task<bool> ShowDeleteConfirmationAsync(int count)
        {
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要将选中的 {count} 个图片文件移至回收站吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public async Task<string?> ShowInputPromptAsync(string title, string message, string defaultText)
        {
            var textBox = new TextBox
            {
                Text = defaultText,
                AcceptsReturn = false,
                MinWidth = 360
            };

            var panel = new StackPanel
            {
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(textBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? textBox.Text : null;
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
            if ((e.OriginalSource as FrameworkElement)?.DataContext is not ImageItem item ||
                string.IsNullOrWhiteSpace(item.ImagePath))
            {
                return;
            }

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
            catch
            {
            }
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.DeleteSelectedCommand.Execute(ImageGrid.SelectedItems);
        }

        private async void OnSortCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "排序",
                new ActionOption("名称升序", "\uE8CB", () => ApplySortAsync("NameAsc")),
                new ActionOption("名称降序", "\uE8CB", () => ApplySortAsync("NameDesc")),
                new ActionOption("日期升序", "\uE787", () => ApplySortAsync("DateAsc")),
                new ActionOption("日期降序", "\uE787", () => ApplySortAsync("DateDesc")),
                new ActionOption("大小升序", "\uE8A9", () => ApplySortAsync("SizeAsc")),
                new ActionOption("大小降序", "\uE8A9", () => ApplySortAsync("SizeDesc")));
        }

        private async void OnDuplicateCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "扫描去重",
                new ActionOption("精确扫描", "\uE721", () => ScanDuplicatesAsync("Exact")),
                new ActionOption("智能扫描", "\uE721", () => ScanDuplicatesAsync("Similar")));
        }

        private async void OnFormatCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "格式转换",
                new ActionOption("转 JPEG", "\uE8B9", () => ViewModel.ConvertSelectedImagesToTargetAsync(ImageGrid.SelectedItems, "Jpeg")),
                new ActionOption("转 PNG", "\uE8B9", () => ViewModel.ConvertSelectedImagesToTargetAsync(ImageGrid.SelectedItems, "Png")),
                new ActionOption("转 BMP", "\uE8B9", () => ViewModel.ConvertSelectedImagesToTargetAsync(ImageGrid.SelectedItems, "Bmp")));
        }

        private async void OnResizeCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "尺寸调整",
                new ActionOption("长边 1280", "\uE740", () => ViewModel.ResizeSelectedImagesAsync(ImageGrid.SelectedItems, "LongEdge1280")),
                new ActionOption("长边 1920", "\uE740", () => ViewModel.ResizeSelectedImagesAsync(ImageGrid.SelectedItems, "LongEdge1920")),
                new ActionOption("长边 2560", "\uE740", () => ViewModel.ResizeSelectedImagesAsync(ImageGrid.SelectedItems, "LongEdge2560")));
        }

        private async void OnCropCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "裁剪",
                new ActionOption("1:1", "\uE7A8", () => ViewModel.CropSelectedImagesAsync(ImageGrid.SelectedItems, "Square")),
                new ActionOption("4:3", "\uE7A8", () => ViewModel.CropSelectedImagesAsync(ImageGrid.SelectedItems, "Ratio4x3")),
                new ActionOption("16:9", "\uE7A8", () => ViewModel.CropSelectedImagesAsync(ImageGrid.SelectedItems, "Ratio16x9")));
        }

        private async void OnCompressCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "压缩导出",
                new ActionOption("轻度压缩", "\uE9D9", () => ViewModel.CompressSelectedImagesAsync(ImageGrid.SelectedItems, "Light")),
                new ActionOption("均衡压缩", "\uE9D9", () => ViewModel.CompressSelectedImagesAsync(ImageGrid.SelectedItems, "Balanced")),
                new ActionOption("高压缩", "\uE9D9", () => ViewModel.CompressSelectedImagesAsync(ImageGrid.SelectedItems, "Aggressive")));
        }

        private async void OnEnhanceCardClicked(object sender, RoutedEventArgs e)
        {
            await ShowActionDialogAsync(
                "AI 增强",
                new ActionOption("智能增强", "\uE945", () => ViewModel.EnhanceSelectedImagesAsync(ImageGrid.SelectedItems, "SmartFix")),
                new ActionOption("清晰增强", "\uE945", () => ViewModel.EnhanceSelectedImagesAsync(ImageGrid.SelectedItems, "DetailBoost")),
                new ActionOption("低光优化", "\uE945", () => ViewModel.EnhanceSelectedImagesAsync(ImageGrid.SelectedItems, "LowLight")));
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

        private void FunctionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFunctionFilter();
        }

        private void MediaManagerPage_Loaded(object sender, RoutedEventArgs e)
        {
            RegisterFeatureCards();
            ApplyFunctionFilter();
            UpdateWorkspaceLayout();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MediaManagerViewModel.HasImages))
            {
                DispatcherQueue.TryEnqueue(UpdateWorkspaceLayout);
            }
        }

        private void UpdateWorkspaceLayout()
        {
            if (ViewModel.HasImages)
            {
                FunctionPanelRow.Height = GridLength.Auto;
                FunctionScrollViewer.MaxHeight = 372;
                ImageGalleryRow.Height = new GridLength(1, GridUnitType.Star);
                ImageGalleryPanel.Visibility = Visibility.Visible;
                return;
            }

            FunctionPanelRow.Height = new GridLength(1, GridUnitType.Star);
            FunctionScrollViewer.MaxHeight = double.PositiveInfinity;
            ImageGalleryRow.Height = new GridLength(0);
            ImageGalleryPanel.Visibility = Visibility.Collapsed;
        }

        private void RegisterFeatureCards()
        {
            if (_featureCards.Count > 0)
            {
                return;
            }

            _featureSections = new (FrameworkElement Section, IReadOnlyList<Button> Cards)[]
            {
                (QuickSection, new[] { ImportSourceCard, SortCard, RenameCard }),
                (OrganizeSection, new[] { DuplicateCard }),
                (ProcessSection, new[] { FormatCard, ResizeCard, CropCard, CompressCard, EnhanceCard }),
                (ManageSection, new[] { OpenLocationCard, ResultCard, TagCard, DeleteCard })
            };

            _featureCards = _featureSections
                .SelectMany(section => section.Cards)
                .ToList();
        }

        private void ApplyFunctionFilter()
        {
            RegisterFeatureCards();

            string query = FunctionSearchBox?.Text?.Trim() ?? string.Empty;
            bool anyVisible = false;

            foreach (var card in _featureCards)
            {
                bool isVisible = IsFunctionCardMatch(card, query);
                card.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                anyVisible |= isVisible;
            }

            foreach (var section in _featureSections)
            {
                section.Section.Visibility = section.Cards.Any(card => card.Visibility == Visibility.Visible)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            NoFunctionResultPanel.Visibility = anyVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private static bool IsFunctionCardMatch(Button card, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string keywords = card.Tag?.ToString() ?? string.Empty;
            string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tokens.All(token => keywords.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private async Task ShowActionDialogAsync(string title, params ActionOption[] options)
        {
            ActionOption? selectedOption = null;
            var dialog = new ContentDialog
            {
                Title = title,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var grid = new Grid
            {
                MinWidth = 360,
                ColumnSpacing = 10,
                RowSpacing = 10
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < options.Length; i++)
            {
                int row = i / 2;
                int column = i % 2;
                if (grid.RowDefinitions.Count <= row)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                var option = options[i];
                var button = CreateDialogOptionButton(option);
                button.Click += (_, _) =>
                {
                    selectedOption = option;
                    dialog.Hide();
                };

                Grid.SetRow(button, row);
                Grid.SetColumn(button, column);
                grid.Children.Add(button);
            }

            dialog.Content = grid;
            await dialog.ShowAsync();

            if (selectedOption != null)
            {
                await selectedOption.ExecuteAsync();
            }
        }

        private static Button CreateDialogOptionButton(ActionOption option)
        {
            var icon = new FontIcon
            {
                Glyph = option.Glyph,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = option.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };
            content.Children.Add(icon);
            content.Children.Add(text);

            return new Button
            {
                MinHeight = 52,
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = content
            };
        }

        private Task ApplySortAsync(string option)
        {
            ViewModel.ApplySortCommand.Execute(option);
            return Task.CompletedTask;
        }

        private Task ScanDuplicatesAsync(string mode)
        {
            ViewModel.ScanDuplicatesCommand.Execute(mode);
            return Task.CompletedTask;
        }

        private sealed record ActionOption(string Title, string Glyph, Func<Task> ExecuteAsync);
    }
}
