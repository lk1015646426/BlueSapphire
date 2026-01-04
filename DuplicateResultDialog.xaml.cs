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
        private ObservableCollection<DuplicateItem> _flatList;

        public DuplicateResultDialog(List<List<StorageFile>> dupes, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            this.InitializeComponent();

            _flatList = new ObservableCollection<DuplicateItem>();
            foreach (var g in dupes)
            {
                _flatList.Add(DuplicateItem.CreateSeparator());
                var sorted = g.OrderByDescending(f => f.DateCreated).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    _flatList.Add(new DuplicateItem(sorted[i], i == 0));
                }
            }
            if (_flatList.Any()) _flatList.RemoveAt(0);

            DuplicateList.ItemsSource = _flatList;
        }

        public List<StorageFile> GetSelectedFiles()
        {
            return _flatList.Where(x => x.File != null && x.IsChecked)
                            .Select(x => x.File!)
                            .ToList();
        }

        private void DuplicateList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is DuplicateItem item && !item.IsSeparator && !args.InRecycleQueue)
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