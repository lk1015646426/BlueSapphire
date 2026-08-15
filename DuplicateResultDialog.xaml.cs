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

        public DuplicateResultDialog(
            List<List<StorageFile>> dupes,
            bool isSimilarScan,
            Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            this.InitializeComponent();
            Title = isSimilarScan ? "相似图片候选结果" : "完全重复文件结果";
            GuidanceText.Text = isSimilarScan
                ? "相似结果来自视觉指纹，只表示外观接近，并不代表内容完全相同。第一项仅按文件大小提供弱保留建议，请双击预览并逐项确认。"
                : "这些文件已通过文件大小、快速指纹和 SHA-256 完整内容指纹校验。内容相同，但仍请根据路径和用途决定保留哪一份。";

            _groupedList = new ObservableCollection<DuplicateGroup>();
            int groupIndex = 1;

            foreach (var g in dupes)
            {
                var group = new DuplicateGroup(
                    $"{(isSimilarScan ? "相似候选组" : "完全重复组")} {groupIndex++}");

                var sorted = g;

                for (int i = 0; i < sorted.Count; i++)
                {
                    // 后端只提供保留建议，不代替用户作出删除决定。
                    group.Add(new DuplicateItem(sorted[i], isSimilarScan && i == 0));
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

        private void DuplicateItem_SelectionChanged(object sender, RoutedEventArgs e)
        {
            int selectedCount = _groupedList.SelectMany(group => group).Count(item => item.IsChecked);
            IsPrimaryButtonEnabled = selectedCount > 0;
            PrimaryButtonText = selectedCount > 0
                ? $"移至回收站（{selectedCount}）"
                : "移至回收站";
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
                catch (Exception)
                {
                    // 外部打开失败常见于文件已被移动或无关联程序，双击预览属辅助操作，不阻断列表操作。
                }
            }
        }
    }
}
