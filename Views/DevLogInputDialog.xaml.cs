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
            FileStatusText.Foreground =
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentSafe"];
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
                try
                {
                    const ulong maxDocumentBytes = 1024 * 1024;
                    var properties = await file.GetBasicPropertiesAsync();
                    if (properties.Size > maxDocumentBytes)
                    {
                        FileStatusText.Text = "[读取失败] 文档不能超过 1 MB";
                        FileStatusText.Foreground =
                            (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentDanger"];
                        return;
                    }

                    string text = await FileIO.ReadTextAsync(file);
                    if (text.Length > 100_000)
                    {
                        FileStatusText.Text = "[读取失败] 文档正文不能超过 100,000 个字符";
                        FileStatusText.Foreground =
                            (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentDanger"];
                        return;
                    }
                    _fullContent = text;
                    FullContentInput.Text = text;

                    FileStatusText.Text = $"[读取成功] {file.Name} ({properties.Size:N0} 字节)";
                    FileStatusText.Foreground =
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentSafe"];
                }
                catch (Exception ex)
                {
                    FileStatusText.Text = $"[读取失败] {ex.Message}";
                    FileStatusText.Foreground =
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentDanger"];
                }
            }
            else
            {
                FileStatusText.Text = "[操作已取消]";
            }
        }

        private void ContentDialog_PrimaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args)
        {
            string title = TitleInput.Text.Trim();
            string version = VersionInput.Text.Trim();
            if (title.Length is 0 or > 200)
            {
                args.Cancel = true;
                FileStatusText.Text = "[无法保存] 标题不能为空且不能超过 200 个字符";
                FileStatusText.Foreground =
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentDanger"];
                TitleInput.Focus(FocusState.Programmatic);
                return;
            }
            if (version.Length > 50 ||
                DescInput.Text.Length > 2000 ||
                FullContentInput.Text.Length > 100_000)
            {
                args.Cancel = true;
                FileStatusText.Text = "[无法保存] 版本、摘要或正文超过长度限制";
                FileStatusText.Foreground =
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentDanger"];
                return;
            }
            if (!string.IsNullOrWhiteSpace(DateInput.Text) &&
                !DateTime.TryParse(DateInput.Text, out _))
            {
                args.Cancel = true;
                FileStatusText.Text = "[无法保存] 日期格式无效，请使用“年-月-日”";
                FileStatusText.Foreground =
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentDanger"];
                DateInput.Focus(FocusState.Programmatic);
            }
        }
    }
}
