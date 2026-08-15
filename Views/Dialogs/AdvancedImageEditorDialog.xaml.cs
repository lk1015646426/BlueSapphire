using BlueSapphire.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Input;

namespace BlueSapphire.Views.Dialogs
{
    public sealed partial class AdvancedImageEditorDialog : ContentDialog
    {
        private readonly IList<string> _imagePaths;
        private readonly bool _isBatch;
        private double _imageWidth;
        private double _imageHeight;
        private Rect _cropRect;
        private double _fixedRatio = 0; // 0 means free
        private bool _isUpdatingSizeBoxes = false;

        public AdvancedEditOptions Options { get; private set; }

        public AdvancedImageEditorDialog(IList<string> imagePaths)
        {
            this.InitializeComponent();
            _imagePaths = imagePaths;
            _isBatch = _imagePaths.Count > 1;
            Options = new AdvancedEditOptions();

            if (_isBatch)
            {
                BatchInfoBar.IsOpen = true;
            }
        }

        private static void SetThumbCursor(Thumb thumb, InputSystemCursorShape shape)
        {
            typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(thumb, InputSystemCursor.Create(shape));
        }

        private void ContentDialog_Loaded(object _sender, RoutedEventArgs e)
        {
            if (_imagePaths.Count > 0 && !string.IsNullOrEmpty(_imagePaths[0]))
            {
                var bitmap = new BitmapImage(new Uri(_imagePaths[0]));
                bitmap.ImageOpened += (s, args) =>
                {
                    _imageWidth = bitmap.PixelWidth;
                    _imageHeight = bitmap.PixelHeight;
                    if (EnableReshapeToggle.IsOn) ResetCropRect();
                    
                    long totalBytes = 0;
                    foreach (var path in _imagePaths)
                    {
                        // 单文件大小读取失败按 0 累计，不影响统计主流程。
                        try { totalBytes += new System.IO.FileInfo(path).Length; } catch {}
                    }
                    string sizeStr = FormatBytes(totalBytes);

                    if (!_isBatch)
                    {
                        OriginalSizeText.Text = $"{_imageWidth} x {_imageHeight} px ({sizeStr})";
                    }
                    else
                    {
                        OriginalSizeText.Text = $"多张图片 (总共 {sizeStr})";
                        OutputWidthBox.IsEnabled = false;
                        OutputHeightBox.IsEnabled = false;
                        OutputWidthBox.PlaceholderText = "批量时由比例决定";
                        OutputHeightBox.PlaceholderText = "批量时由比例决定";
                    }
                };
                PreviewImage.Source = bitmap;
            }
            
            // Attach NumberBox events dynamically to avoid XamlCompiler bugs with TypedEventHandler
            OutputWidthBox.ValueChanged += OutputSizeBox_ValueChanged;
            OutputHeightBox.ValueChanged += OutputSizeBox_ValueChanged;

            // Set up cursors using reflection because ProtectedCursor is protected
            SetThumbCursor(CenterDragThumb, InputSystemCursorShape.SizeAll);
            
            SetThumbCursor(ThumbTL, InputSystemCursorShape.SizeNorthwestSoutheast);
            SetThumbCursor(ThumbBR, InputSystemCursorShape.SizeNorthwestSoutheast);
            SetThumbCursor(ThumbTR, InputSystemCursorShape.SizeNortheastSouthwest);
            SetThumbCursor(ThumbBL, InputSystemCursorShape.SizeNortheastSouthwest);

            SetThumbCursor(ThumbT, InputSystemCursorShape.SizeNorthSouth);
            SetThumbCursor(ThumbB, InputSystemCursorShape.SizeNorthSouth);
            SetThumbCursor(ThumbL, InputSystemCursorShape.SizeWestEast);
            SetThumbCursor(ThumbR, InputSystemCursorShape.SizeWestEast);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.##") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.##") + " MB";
        }

        private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PreviewImage.ActualWidth > 0 && PreviewImage.ActualHeight > 0)
            {
                OuterMaskRect.Rect = new Rect(0, 0, PreviewImage.ActualWidth, PreviewImage.ActualHeight);
                CropCanvas.Width = PreviewImage.ActualWidth;
                CropCanvas.Height = PreviewImage.ActualHeight;
                if (EnableReshapeToggle.IsOn) ResetCropRect();
            }
        }

        private void EnableReshapeToggle_Toggled(object _sender, RoutedEventArgs e)
        {
            if (ReshapePanel == null || CropCanvas == null) return;
            
            bool isEnabled = EnableReshapeToggle.IsOn;
            ReshapePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            CropCanvas.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (isEnabled)
            {
                ResetCropRect();
            }
        }

        private void EnableTargetSizeToggle_Toggled(object _sender, RoutedEventArgs e)
        {
            if (TargetSizePanel == null) return;
            
            TargetSizePanel.Visibility = EnableTargetSizeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RatioBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                UncheckAllRatios();
                btn.IsChecked = true;

                if (btn.Tag is string tagString && double.TryParse(tagString, out double ratio))
                {
                    _fixedRatio = ratio;
                    if (EnableReshapeToggle.IsOn) ResetCropRect();
                }
            }
        }

        private void UncheckAllRatios()
        {
            RatioBtn1x1.IsChecked = false;
            RatioBtn3x4.IsChecked = false;
            RatioBtn4x3.IsChecked = false;
            RatioBtn9x16.IsChecked = false;
            RatioBtn16x9.IsChecked = false;
            RatioBtn2x3.IsChecked = false;
            RatioBtn3x2.IsChecked = false;
        }

        private void OutputSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isUpdatingSizeBoxes || _isBatch || PreviewImage.ActualWidth == 0 || PreviewImage.ActualHeight == 0) return;

            // When user types explicit pixel sizes, we lock the crop box proportionally or adjust it.
            // For now, if they type a size, we just ensure it sets the aspect ratio if fixed.
            if (_fixedRatio > 0 && sender.Value > 0)
            {
                _isUpdatingSizeBoxes = true;
                if (sender == OutputWidthBox)
                {
                    OutputHeightBox.Value = Math.Round(OutputWidthBox.Value / _fixedRatio);
                }
                else if (sender == OutputHeightBox)
                {
                    OutputWidthBox.Value = Math.Round(OutputHeightBox.Value * _fixedRatio);
                }
                _isUpdatingSizeBoxes = false;
            }
        }

        private void ResetCropButton_Click(object sender, RoutedEventArgs e)
        {
            ResetCropRect();
        }

        private void ResetCropRect()
        {
            if (PreviewImage.ActualWidth == 0 || PreviewImage.ActualHeight == 0) return;

            double w = PreviewImage.ActualWidth;
            double h = PreviewImage.ActualHeight;

            if (_fixedRatio > 0)
            {
                double targetW = w;
                double targetH = h;
                
                if (w / h > _fixedRatio)
                {
                    targetW = h * _fixedRatio;
                }
                else
                {
                    targetH = w / _fixedRatio;
                }

                double x = (w - targetW) / 2;
                double y = (h - targetH) / 2;
                _cropRect = new Rect(x, y, targetW, targetH);
            }
            else
            {
                _cropRect = new Rect(w * 0.05, h * 0.05, w * 0.9, h * 0.9);
            }

            UpdateCropVisuals();
        }

        private void UpdateCropVisuals()
        {
            InnerCropRect.Rect = _cropRect;
            
            Canvas.SetLeft(CropBorder, _cropRect.X);
            Canvas.SetTop(CropBorder, _cropRect.Y);
            CropBorder.Width = _cropRect.Width;
            CropBorder.Height = _cropRect.Height;

            CenterDragThumb.Width = _cropRect.Width;
            CenterDragThumb.Height = _cropRect.Height;
            Canvas.SetLeft(CenterDragThumb, _cropRect.X);
            Canvas.SetTop(CenterDragThumb, _cropRect.Y);

            UpdateThumb(ThumbTL, _cropRect.X, _cropRect.Y);
            UpdateThumb(ThumbTR, _cropRect.Right, _cropRect.Y);
            UpdateThumb(ThumbBL, _cropRect.X, _cropRect.Bottom);
            UpdateThumb(ThumbBR, _cropRect.Right, _cropRect.Bottom);

            ThumbT.Width = _cropRect.Width;
            Canvas.SetLeft(ThumbT, _cropRect.X);
            Canvas.SetTop(ThumbT, _cropRect.Y - ThumbT.Height / 2);

            ThumbB.Width = _cropRect.Width;
            Canvas.SetLeft(ThumbB, _cropRect.X);
            Canvas.SetTop(ThumbB, _cropRect.Bottom - ThumbB.Height / 2);

            ThumbL.Height = _cropRect.Height;
            Canvas.SetLeft(ThumbL, _cropRect.X - ThumbL.Width / 2);
            Canvas.SetTop(ThumbL, _cropRect.Y);

            ThumbR.Height = _cropRect.Height;
            Canvas.SetLeft(ThumbR, _cropRect.Right - ThumbR.Width / 2);
            Canvas.SetTop(ThumbR, _cropRect.Y);

            UpdateOutputSizeDisplay();
        }

        private void UpdateOutputSizeDisplay()
        {
            if (_isBatch || PreviewImage.ActualWidth == 0 || PreviewImage.ActualHeight == 0) return;

            _isUpdatingSizeBoxes = true;
            double scaleX = _imageWidth / PreviewImage.ActualWidth;
            double scaleY = _imageHeight / PreviewImage.ActualHeight;

            OutputWidthBox.Value = Math.Round(_cropRect.Width * scaleX);
            OutputHeightBox.Value = Math.Round(_cropRect.Height * scaleY);
            _isUpdatingSizeBoxes = false;
        }

        private static void UpdateThumb(Thumb thumb, double x, double y)
        {
            Canvas.SetLeft(thumb, x - thumb.Width / 2);
            Canvas.SetTop(thumb, y - thumb.Height / 2);
        }

        private void CenterDragThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newX = _cropRect.X + e.HorizontalChange;
            double newY = _cropRect.Y + e.VerticalChange;

            if (newX < 0) newX = 0;
            if (newY < 0) newY = 0;
            if (newX + _cropRect.Width > PreviewImage.ActualWidth) newX = PreviewImage.ActualWidth - _cropRect.Width;
            if (newY + _cropRect.Height > PreviewImage.ActualHeight) newY = PreviewImage.ActualHeight - _cropRect.Height;

            _cropRect.X = newX;
            _cropRect.Y = newY;
            UpdateCropVisuals();
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // Set to free mode when corners are dragged
            _fixedRatio = 0;
            UncheckAllRatios();

            if (sender is Thumb thumb && thumb.Tag is string tag)
            {
                double minSize = 20;
                double newX = _cropRect.X;
                double newY = _cropRect.Y;
                double newW = _cropRect.Width;
                double newH = _cropRect.Height;

                if (tag.Contains('L'))
                {
                    double dx = Math.Max(-newX, Math.Min(e.HorizontalChange, newW - minSize));
                    newX += dx;
                    newW -= dx;
                }
                else if (tag.Contains('R'))
                {
                    double dx = Math.Min(PreviewImage.ActualWidth - _cropRect.Right, Math.Max(e.HorizontalChange, minSize - newW));
                    newW += dx;
                }

                if (tag.Contains('T'))
                {
                    double dy = Math.Max(-newY, Math.Min(e.VerticalChange, newH - minSize));
                    newY += dy;
                    newH -= dy;
                }
                else if (tag.Contains('B'))
                {
                    double dy = Math.Min(PreviewImage.ActualHeight - _cropRect.Bottom, Math.Max(e.VerticalChange, minSize - newH));
                    newH += dy;
                }

                if (_fixedRatio > 0)
                {
                    if (tag == "TR" || tag == "BR") newH = newW / _fixedRatio;
                    else newW = newH * _fixedRatio;
                    
                    if (newX + newW > PreviewImage.ActualWidth) newW = PreviewImage.ActualWidth - newX;
                    if (newY + newH > PreviewImage.ActualHeight) newH = PreviewImage.ActualHeight - newY;

                    if (tag.Contains('T')) newY = _cropRect.Bottom - newH;
                    if (tag.Contains('L')) newX = _cropRect.Right - newW;
                }

                _cropRect = new Rect(newX, newY, newW, newH);
                UpdateCropVisuals();
            }
        }

        private bool _isDraggingCrop = false;
        private Point _dragStartPoint;
        private Rect _cropRectStart;

        private void CropCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(CropCanvas).Position;
            if (_cropRect.Contains(pos))
            {
                _isDraggingCrop = true;
                _dragStartPoint = pos;
                _cropRectStart = _cropRect;
                CropCanvas.CapturePointer(e.Pointer);
            }
        }

        private void CropCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingCrop)
            {
                var pos = e.GetCurrentPoint(CropCanvas).Position;
                double dx = pos.X - _dragStartPoint.X;
                double dy = pos.Y - _dragStartPoint.Y;

                double newX = _cropRectStart.X + dx;
                double newY = _cropRectStart.Y + dy;

                if (newX < 0) newX = 0;
                if (newY < 0) newY = 0;
                if (newX + _cropRect.Width > PreviewImage.ActualWidth) newX = PreviewImage.ActualWidth - _cropRect.Width;
                if (newY + _cropRect.Height > PreviewImage.ActualHeight) newY = PreviewImage.ActualHeight - _cropRect.Height;

                _cropRect.X = newX;
                _cropRect.Y = newY;
                UpdateCropVisuals();
            }
        }

        private void CropCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingCrop)
            {
                _isDraggingCrop = false;
                CropCanvas.ReleasePointerCapture(e.Pointer);
            }
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Options.IsCropEnabled = EnableReshapeToggle.IsOn;
            if (Options.IsCropEnabled)
            {
                if (_isBatch)
                {
                    Options.CropAspectRatio = _fixedRatio > 0 ? _fixedRatio : 1.0;
                    Options.UseExactCrop = false;
                }
                else
                {
                    if (_fixedRatio > 0)
                    {
                        Options.CropAspectRatio = _fixedRatio;
                        Options.UseExactCrop = false;
                    }
                    else
                    {
                        Options.UseExactCrop = true;
                        double scaleX = _imageWidth / PreviewImage.ActualWidth;
                        double scaleY = _imageHeight / PreviewImage.ActualHeight;

                        Options.ExactCropX = (uint)Math.Max(0, _cropRect.X * scaleX);
                        Options.ExactCropY = (uint)Math.Max(0, _cropRect.Y * scaleY);
                        Options.ExactCropWidth = (uint)(_cropRect.Width * scaleX);
                        Options.ExactCropHeight = (uint)(_cropRect.Height * scaleY);
                    }
                }
            }

            Options.TargetWidth = (uint)Math.Max(0, OutputWidthBox.Value);
            Options.TargetHeight = (uint)Math.Max(0, OutputHeightBox.Value);
            Options.KeepAspectRatio = true; // Always keep ratio for this logic

            Options.IsTargetSizeEnabled = EnableTargetSizeToggle.IsOn;
            if (Options.IsTargetSizeEnabled)
            {
                long multiplier = TargetSizeUnitCombo.SelectedIndex == 1 ? 1024 * 1024 : 1024;
                Options.TargetMinFileSizeBytes = (long)Math.Max(1, MinSizeBox.Value) * multiplier;
                Options.TargetMaxFileSizeBytes = (long)Math.Max(1, MaxSizeBox.Value) * multiplier;
            }
        }
    }
}
