using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire
{
    public sealed partial class MediaManagerPage : Page, ITool, IMediaViewInteraction
    {
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;
        public Type ContentPage => typeof(MediaManagerPage);

        // 公开 ViewModel 供 x:Bind 使用
        public MediaManagerViewModel ViewModel { get; }

        public MediaManagerPage()
        {
            this.InitializeComponent();
            // 初始化 ViewModel，注入 View 接口和 Dispatcher
            ViewModel = new MediaManagerViewModel(this, this.DispatcherQueue);
        }

        public void Initialize() { }

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
            // 将选中的项传递给 ViewModel
            // 注意：GridView.SelectedItems 是弱类型的，需要手动传递
            var items = ImageGrid.SelectedItems;
            ViewModel.DeleteSelectedCommand.Execute(items);
        }

        // --- IMediaViewInteraction 接口实现 (UI 逻辑) ---

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

            if (MainWindow.Instance != null)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance));
            }
            return await openPicker.PickSingleFolderAsync();
        }

        public async Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> dupes)
        {
            // 构建 UI 数据源 (DuplicateItem)
            var flatList = new System.Collections.ObjectModel.ObservableCollection<DuplicateItem>();
            foreach (var g in dupes)
            {
                flatList.Add(DuplicateItem.CreateSeparator());
                // 按日期降序，保留最新的一个
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    // i==0 (最新的) 建议保留 (IsKeepSuggestion=true)
                    flatList.Add(new DuplicateItem(sorted[i], i == 0));
                }
            }
            if (flatList.Any()) flatList.RemoveAt(0);

            // 动态创建 ListView
            var listView = new ListView
            {
                ItemsSource = flatList,
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 400
            };

            // 使用 XamlReader 加载 DataTemplate (保持原逻辑)
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