using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.ViewModels;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogPage : Page
    {
        public DevLogViewModel ViewModel { get; } = new DevLogViewModel();

        public DevLogPage()
        {
            this.InitializeComponent();
        }

        private async void OpenInputDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DevLogInputDialog
            {
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.AddNewLogAsync(dialog.NodeTitle, dialog.NodeDescription, dialog.NodeVersion);
            }
        }
    }
}