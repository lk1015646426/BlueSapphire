using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogInputDialog : ContentDialog
    {
        public string NodeTitle => TitleInput.Text;
        public string NodeDescription => DescInput.Text;
        public string NodeVersion => VersionInput.Text;

        // 核心修复：用原生变量替代 TextBox 存储完整文本，防止遇到回车被强行截断
        private string _fullContent = string.Empty;
        public string NodeFullContent => _fullContent;

        public DateTime? NodeDate
        {
            get
            {
                if (DateTime.TryParse(DateInput.Text, out DateTime result))
                    return result;
                return null;
            }
        }

        public DevLogInputDialog()
        {
            this.InitializeComponent();
        }

        private async void SelectTxtFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            // 获取宿主窗口句柄
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                // 彻底解决截断问题：直接把读到的完整文本塞给内部变量
                string text = await FileIO.ReadTextAsync(file);
                _fullContent = text;

                FileStatusText.Text = $"[已加载] {file.Name} ({text.Length} 字节)";
                FileStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Cyan);
            }
            else
            {
                FileStatusText.Text = "[操作取消]";
            }
        }
    }
}