using BlueSapphire.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage;

namespace BlueSapphire
{
    public sealed partial class DuplicateResultDialog : ContentDialog
    {
        // ✅ 改用原生的分组集合
        private ObservableCollection<DuplicateGroup> _groupedList;

        public DuplicateResultDialog(List<List<StorageFile>> dupes, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            this.InitializeComponent();

            _groupedList = new ObservableCollection<DuplicateGroup>();
            int groupIndex = 1;

            foreach (var g in dupes)
            {
                var group = new DuplicateGroup($"重复文件组 {groupIndex++}");

                // ✅ 极客优化：绝对信任后端 Service 传来的排序。
                // 精确模式下无所谓，但在智能模式下，Service 已经把体积最大（画质最好）的放到了第 0 位！
                var sorted = g;

                for (int i = 0; i < sorted.Count; i++)
                {
                    // i == 0 的项会被标记为 IsKeepSuggestion = true (推荐保留)
                    group.Add(new DuplicateItem(sorted[i], i == 0));
                }
                _groupedList.Add(group);
            }

            // ✅ 将数据源绑定到 XAML 中定义的 CollectionViewSource
            GroupedItems.Source = _groupedList;
        }

        public List<StorageFile> GetSelectedFiles()
        {
            // ✅ 使用 SelectMany 将所有分组展平，提取被勾选的项
            return _groupedList.SelectMany(g => g)
                               .Where(x => x.IsChecked)
                               .Select(x => x.File)
                               .ToList();
        }

        private void DuplicateList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            // ✅ 已经没有假分割线了，去掉了 !item.IsSeparator 的判断
            if (args.Item is DuplicateItem item && !args.InRecycleQueue)
            {
                _ = item.LoadThumbnailAsync(this.DispatcherQueue);
            }
        }

        private async void DuplicateList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is DuplicateItem item
                && item.File != null)
            {
                try
                {
                    await Windows.System.Launcher.LaunchFileAsync(item.File);
                }
                catch { }
            }
        }
    }
}