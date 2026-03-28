using System;
using BlueSapphire.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BlueSapphire
{
    public sealed partial class AudioTrimDialog : ContentDialog
    {
        private readonly TimeSpan? _duration;
        private readonly bool _isBatch;

        public AudioTrimRequest? Request { get; private set; }

        public AudioTrimDialog(string fileName, TimeSpan? duration, bool isBatch = false)
        {
            this.InitializeComponent();

            _isBatch = isBatch;
            _duration = duration > TimeSpan.Zero ? duration : null;
            FileNameTextBlock.Text = fileName;
            DurationTextBlock.Text = _duration.HasValue
                ? $"音频时长：{FormatDuration(_duration.Value)}"
                : "音频时长：未读取到元数据，可手动输入裁剪范围。";
            ModeHintTextBlock.Text = isBatch
                ? "会对所有选中的音频导出同一时间区间的片段；原始音频不会被覆盖。"
                : "会生成一个新的裁剪文件，原始音频不会被覆盖。";

            StartTextBox.Text = "00:00";
            EndTextBox.Text = _duration.HasValue
                ? FormatDuration(_duration.Value)
                : "00:30";

            UpdateValidationState();
        }

        private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateValidationState();
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            UpdateValidationState();
            args.Cancel = Request == null;
        }

        private void UpdateValidationState()
        {
            if (!AudioTrimRequest.TryCreate(StartTextBox.Text, EndTextBox.Text, out AudioTrimRequest? request, out string validationMessage))
            {
                Request = null;
                IsPrimaryButtonEnabled = false;
                ValidationTextBlock.Text = validationMessage;
                ValidationTextBlock.Visibility = Visibility.Visible;
                RangeHintTextBlock.Text = "请先设置有效的裁剪区间。";
                return;
            }

            if (request == null)
            {
                Request = null;
                IsPrimaryButtonEnabled = false;
                ValidationTextBlock.Text = "未能创建有效的裁剪区间。";
                ValidationTextBlock.Visibility = Visibility.Visible;
                RangeHintTextBlock.Text = "请重新输入裁剪区间。";
                return;
            }

            if (_duration.HasValue && request.EndTime > _duration.Value)
            {
                Request = null;
                IsPrimaryButtonEnabled = false;
                ValidationTextBlock.Text = $"结束时间不能超过音频总时长 {FormatDuration(_duration.Value)}。";
                ValidationTextBlock.Visibility = Visibility.Visible;
                RangeHintTextBlock.Text = "请调整裁剪区间。";
                return;
            }

            Request = request;
            IsPrimaryButtonEnabled = true;
            ValidationTextBlock.Text = string.Empty;
            ValidationTextBlock.Visibility = Visibility.Collapsed;
            RangeHintTextBlock.Text = _isBatch
                ? $"将对所选音频统一导出区间：{request.RangeText}（时长 {FormatDuration(request.Duration)}）"
                : $"将导出区间：{request.RangeText}（时长 {FormatDuration(request.Duration)}）";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? duration.ToString(@"hh\:mm\:ss")
                : duration.ToString(@"mm\:ss");
        }
    }
}
