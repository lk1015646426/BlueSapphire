using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using BlueSapphire.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, IMediaViewInteraction
    {


        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<MediaManagerViewModel>();
            InitializeComponent();
            ViewModel.Initialize(this, DispatcherQueue);
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
                : [];
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

        private void OnSortFlyoutItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string option)
            {
                ApplySortAsync(option);
            }
        }

        private void OnDuplicateFlyoutItemClicked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string mode)
            {
                ScanDuplicatesAsync(mode);
            }
        }

        private void ScrollViewer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                var properties = e.GetCurrentPoint(scrollViewer).Properties;
                if (properties.IsHorizontalMouseWheel) return;

                double delta = properties.MouseWheelDelta;
                scrollViewer.ChangeView(scrollViewer.HorizontalOffset - delta, null, null);
                e.Handled = true;
            }
        }

        public async Task<FormatConvertOptions?> ShowFormatConvertDialogAsync()
        {
            var dialog = new FormatConvertDialog { XamlRoot = this.XamlRoot };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.Options : null;
        }

        public async Task<AdvancedEditOptions?> ShowAdvancedEditorDialogAsync(IList<string> previewImagePaths)
        {
            var dialog = new AdvancedImageEditorDialog(previewImagePaths) { XamlRoot = this.XamlRoot };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.Options : null;
        }

        public async Task<EnhanceOptions?> ShowEnhanceDialogAsync(string? previewImagePath)
        {
            var dialog = new EnhanceImageDialog(previewImagePath) { XamlRoot = this.XamlRoot };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog.Options : null;
        }

        private void MediaManagerPage_Loaded(object sender, RoutedEventArgs e)
        {
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
