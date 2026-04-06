using BlueSapphire.Models;
using BlueSapphire.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogPage : Page
    {
        public DevLogViewModel ViewModel { get; }

        public DevLogPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<DevLogViewModel>();
            InitializeComponent();
            Loaded += DevLogPage_Loaded;
        }

        private async void DevLogPage_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.EnsureInitializedAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(SettingsPage));
            }
        }

        private async void OpenInputDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DevLogInputDialog { XamlRoot = XamlRoot };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.NodeTitle))
            {
                await ViewModel.AddNewLogAsync(
                    dialog.NodeTitle,
                    dialog.NodeDescription,
                    dialog.NodeVersion,
                    dialog.NodeUpdateLevel,
                    dialog.NodeFullContent,
                    dialog.NodeDate);
            }
        }

        private void DeleteLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is DevLogItem item)
            {
                RootPage.IsTabStop = true;
                RootPage.Focus(FocusState.Programmatic);

                if (ViewModel.DeleteLogCommand.CanExecute(item))
                {
                    ViewModel.DeleteLogCommand.Execute(item);
                }
            }
        }

        private void OpenDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is DevLogItem item)
            {
                DetailTitle.Text = item.Title;
                DetailVersion.Text = $"版本号: {item.Version}  |  更新时间: {item.DisplayTime}";
                DetailContent.Text = string.IsNullOrWhiteSpace(item.FullContent) ? "暂无详细文档内容。" : item.FullContent;
                DetailOverlay.Visibility = Visibility.Visible;
            }
        }

        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            DetailOverlay.Visibility = Visibility.Collapsed;
        }

        private void OpenDetail_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }

        private void OpenDetail_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }
    }
}
