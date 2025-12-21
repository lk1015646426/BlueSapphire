using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using System; // 必须引用，解决 Task 和 GetAwaiter 问题
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // 必须引用
using Windows.Storage; // 必须引用，解决 StorageFolder 问题
using Windows.Foundation; // 必须引用，解决 IAsyncOperation await 问题

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, ITool, IMediaViewInteraction
    {
        // --- ITool 接口实现 (之前报错是因为缺少了这些) ---
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        public void Initialize() { }

        // 公开 ViewModel 供 x:Bind 使用
        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            this.InitializeComponent();
            ViewModel = new MediaManagerViewModel(this, this.DispatcherQueue);
        }

        // --- 事件处理 ---
        private void ImageGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is ImageItem item && !args.InRecycleQueue)
            {
                _ = item.LoadImageAsync(this.DispatcherQueue);
            }
        }

        private void ImageGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ImageGrid.SelectedItem is ImageItem item)
            {
                _ = ShowTipAsync($"文件: {item.FileName}\n路径: {item.ImagePath}\n大小: {item.FileSizeString}");
            }
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            var items = ImageGrid.SelectedItems;
            ViewModel.DeleteSelectedCommand.Execute(items);
        }

        // --- IMediaViewInteraction 接口实现 ---

        public async Task ShowTipAsync(string message)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "提示",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        }

        public async Task<bool> ShowDeleteConfirmationAsync(int count)
        {
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除选中的 {count} 个文件吗？\n此操作不可撤销。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task<StorageFolder?> PickFolderAsync()
        {
            var openPicker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            openPicker.FileTypeFilter.Add("*");

            // [优化] 使用 App.CurrentWindow 替代 MainWindow.Instance
            if (App.CurrentWindow != null)
            {
                // 获取窗口句柄
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);
            }

            // 需要 using System; 和 using Windows.Foundation; 才能 await 这个调用
            return await openPicker.PickSingleFolderAsync();
        }

        public async Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> dupes)
        {
            var flatList = new System.Collections.ObjectModel.ObservableCollection<DuplicateItem>();
            foreach (var g in dupes)
            {
                flatList.Add(DuplicateItem.CreateSeparator());
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    flatList.Add(new DuplicateItem(sorted[i], i == 0));
                }
            }
            if (flatList.Any()) flatList.RemoveAt(0);

            var listView = new ListView
            {
                ItemsSource = flatList,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 400
            };

            string xaml = @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Grid>
                        <TextBlock Text='重复组' FontWeight='Bold' Foreground='#00FFFF' Opacity='0.6' Margin='0,10,0,0'
                                   Visibility='{Binding SeparatorVisibility}'/>     
                        <CheckBox IsChecked='{Binding IsChecked, Mode=TwoWay}' Margin='0,2,0,2'
                                  Visibility='{Binding CheckBoxVisibility}'>
                            <StackPanel Orientation='Horizontal'>
                                <TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold' />
                                <TextBlock Text=' (' Foreground='#80FFFFFF' FontSize='12'/>
                                <TextBlock Text='{Binding DateString}' Foreground='#80FFFFFF' FontSize='12'/>
                                <TextBlock Text=')' Foreground='#80FFFFFF' FontSize='12'/>
                                <TextBlock Text=' [推荐保留]' Foreground='LightGreen' Margin='10,0,0,0' FontSize='12'
                                           Visibility='{Binding SuggestionVisibility}'/>
                            </StackPanel>
                        </CheckBox>
                    </Grid>
                </DataTemplate>";
            listView.ItemTemplate = (DataTemplate)XamlReader.Load(xaml);

            var dialog = new ContentDialog
            {
                Title = $"发现 {dupes.Count} 组重复文件",
                Content = listView,
                PrimaryButtonText = "删除选中项",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                return flatList.Where(x => x.File != null && x.IsChecked).Select(x => x.File!).ToList();
            }
            return new List<StorageFile>();
        }
    }
}