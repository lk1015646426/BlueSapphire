using System;
using BlueSapphire.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BlueSapphire
{
    public sealed partial class AudioTagEditDialog : ContentDialog
    {
        private readonly AudioTagEditSeed _seed;

        public AudioTagEditRequest? Request { get; private set; }

        public AudioTagEditDialog(AudioTagEditSeed seed)
        {
            this.InitializeComponent();
            _seed = seed;

            SummaryTextBlock.Text = seed.IsBatch
                ? $"批量编辑 {seed.ItemCount} 个音频文件的标签"
                : $"编辑音频标签：{seed.PrimaryFileName}";
            HintTextBlock.Text = seed.IsBatch
                ? "勾选后才会写入对应字段。批量模式下不修改标题和曲序，避免误写。"
                : "勾选后才会写入对应字段；留空表示清空该字段。";
            ArtworkHintTextBlock.Text = seed.HasEmbeddedCoverArt
                ? "当前音频已包含封面；封面操作请使用工具栏中的“封面”菜单。"
                : "当前音频未嵌入封面；封面操作请使用工具栏中的“封面”菜单。";

            TitleTextBox.Text = seed.Title ?? string.Empty;
            ArtistTextBox.Text = seed.Artist ?? string.Empty;
            AlbumTextBox.Text = seed.Album ?? string.Empty;
            TrackTextBox.Text = seed.TrackNumber?.ToString() ?? string.Empty;
            YearTextBox.Text = seed.Year?.ToString() ?? string.Empty;
            AlbumArtistTextBox.Text = seed.AlbumArtist ?? string.Empty;
            ComposerTextBox.Text = seed.Composer ?? string.Empty;
            GenreTextBox.Text = seed.Genre ?? string.Empty;
            DiscTextBox.Text = seed.DiscNumber?.ToString() ?? string.Empty;
            CommentTextBox.Text = seed.Comment ?? string.Empty;
            LyricsTextBox.Text = seed.Lyrics ?? string.Empty;

            if (seed.IsBatch)
            {
                TitlePanel.Visibility = Visibility.Collapsed;
                TrackPanel.Visibility = Visibility.Collapsed;
            }

            UpdateFieldEnabledStates();
            UpdateValidationState();
        }

        private void FieldStateChanged(object sender, RoutedEventArgs e)
        {
            UpdateFieldEnabledStates();
            UpdateValidationState();
        }

        private void FieldValueChanged(object sender, TextChangedEventArgs e)
        {
            UpdateValidationState();
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            UpdateValidationState();
            args.Cancel = Request == null;
        }

        private void UpdateFieldEnabledStates()
        {
            TitleTextBox.IsEnabled = ApplyTitleCheckBox.IsChecked == true;
            ArtistTextBox.IsEnabled = ApplyArtistCheckBox.IsChecked == true;
            AlbumTextBox.IsEnabled = ApplyAlbumCheckBox.IsChecked == true;
            AlbumArtistTextBox.IsEnabled = ApplyAlbumArtistCheckBox.IsChecked == true;
            ComposerTextBox.IsEnabled = ApplyComposerCheckBox.IsChecked == true;
            GenreTextBox.IsEnabled = ApplyGenreCheckBox.IsChecked == true;
            TrackTextBox.IsEnabled = ApplyTrackCheckBox.IsChecked == true && !_seed.IsBatch;
            YearTextBox.IsEnabled = ApplyYearCheckBox.IsChecked == true;
            DiscTextBox.IsEnabled = ApplyDiscCheckBox.IsChecked == true;
            CommentTextBox.IsEnabled = ApplyCommentCheckBox.IsChecked == true;
            LyricsTextBox.IsEnabled = ApplyLyricsCheckBox.IsChecked == true;
        }

        private void UpdateValidationState()
        {
            ValidationTextBlock.Visibility = Visibility.Collapsed;
            ValidationTextBlock.Text = string.Empty;

            if (!TryBuildRequest(out AudioTagEditRequest? request, out string validationMessage))
            {
                Request = null;
                IsPrimaryButtonEnabled = false;
                ValidationTextBlock.Text = validationMessage;
                ValidationTextBlock.Visibility = Visibility.Visible;
                return;
            }

            if (request == null || !request.HasChanges)
            {
                Request = null;
                IsPrimaryButtonEnabled = false;
                ValidationTextBlock.Text = "请至少勾选一个需要更新的标签字段。";
                ValidationTextBlock.Visibility = Visibility.Visible;
                return;
            }

            Request = request;
            IsPrimaryButtonEnabled = true;
        }

        private bool TryBuildRequest(out AudioTagEditRequest? request, out string validationMessage)
        {
            request = null;
            validationMessage = string.Empty;

            uint? trackNumber = null;
            if (ApplyTrackCheckBox.IsChecked == true && !_seed.IsBatch)
            {
                if (!TryParseOptionalUInt(TrackTextBox.Text, out trackNumber, out validationMessage))
                {
                    return false;
                }
            }

            uint? year = null;
            if (ApplyYearCheckBox.IsChecked == true)
            {
                if (!TryParseOptionalUInt(YearTextBox.Text, out year, out validationMessage))
                {
                    return false;
                }
            }

            uint? discNumber = null;
            if (ApplyDiscCheckBox.IsChecked == true)
            {
                if (!TryParseOptionalUInt(DiscTextBox.Text, out discNumber, out validationMessage))
                {
                    return false;
                }
            }

            request = new AudioTagEditRequest(
                ApplyTitleCheckBox.IsChecked == true && !_seed.IsBatch,
                TitleTextBox.Text,
                ApplyArtistCheckBox.IsChecked == true,
                ArtistTextBox.Text,
                ApplyAlbumCheckBox.IsChecked == true,
                AlbumTextBox.Text,
                ApplyTrackCheckBox.IsChecked == true && !_seed.IsBatch,
                trackNumber,
                ApplyYearCheckBox.IsChecked == true,
                year,
                ApplyAlbumArtistCheckBox.IsChecked == true,
                AlbumArtistTextBox.Text,
                ApplyComposerCheckBox.IsChecked == true,
                ComposerTextBox.Text,
                ApplyGenreCheckBox.IsChecked == true,
                GenreTextBox.Text,
                ApplyDiscCheckBox.IsChecked == true,
                discNumber,
                ApplyCommentCheckBox.IsChecked == true,
                CommentTextBox.Text,
                ApplyLyricsCheckBox.IsChecked == true,
                LyricsTextBox.Text);

            return true;
        }

        private static bool TryParseOptionalUInt(string? text, out uint? value, out string validationMessage)
        {
            value = null;
            validationMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!uint.TryParse(text.Trim(), out uint parsedValue))
            {
                validationMessage = "曲序、碟序和年份必须是非负整数。";
                return false;
            }

            value = parsedValue;
            return true;
        }
    }
}
