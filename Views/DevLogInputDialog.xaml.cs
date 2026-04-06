using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Models;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogInputDialog : ContentDialog
    {
        public string NodeTitle => TitleInput.Text;
        public string NodeDescription => DescInput.Text;
        public string NodeVersion => VersionInput.Text;

        // 获取纯净的正规中文分级参数
        public string NodeUpdateLevel
        {
            get
            {
                if (LevelInput.SelectedIndex == 0) return "核心跃迁";
                return "常规迭代";
            }
        }

        private string _fullContent = string.Empty;
        public string NodeFullContent => string.IsNullOrWhiteSpace(FullContentInput.Text) ? _fullContent : FullContentInput.Text;

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

        public DevLogInputDialog(DevLogItem item) : this()
        {
            if (item == null)
            {
                return;
            }

            DialogTitleText.Text = "编辑更新记录";
            PrimaryButtonText = "确认修改";

            TitleInput.Text = item.Title;
            DescInput.Text = item.Description;
            VersionInput.Text = item.Version;
            DateInput.Text = item.Timestamp.ToString("yyyy-MM-dd");
            FullContentInput.Text = item.FullContent;
            _fullContent = item.FullContent;
            LevelInput.SelectedIndex = item.UpdateLevel == "核心跃迁" ? 0 : 1;
            FileStatusText.Text = "已加载现有记录，可直接修改后保存";
            FileStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Cyan);
        }

        private async void SelectTxtFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string text = await FileIO.ReadTextAsync(file);
                _fullContent = text;
                FullContentInput.Text = text;

                FileStatusText.Text = $"[读取成功] {file.Name} ({text.Length} 字节)";
                FileStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Cyan);
            }
            else
            {
                FileStatusText.Text = "[操作已取消]";
            }
        }
    }
}
