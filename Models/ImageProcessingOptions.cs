using BlueSapphire.Services;

namespace BlueSapphire.Models
{
    public class FormatConvertOptions
    {
        public ImageConversionTarget TargetFormat { get; set; } = ImageConversionTarget.Jpeg;
        public double Quality { get; set; } = 0.92;
    }

    public class EnhanceOptions
    {
        public double Brightness { get; set; } = 0;
        public double Contrast { get; set; } = 1;
        public double Saturation { get; set; } = 1;
        public double Sharpness { get; set; } = 0;
    }

    public class AdvancedEditOptions
    {
        // Resize options
        public uint TargetWidth { get; set; }
        public uint TargetHeight { get; set; }
        public bool KeepAspectRatio { get; set; } = true;

        // Crop options (Using Ratio for batch)
        public bool IsCropEnabled { get; set; }
        public double CropAspectRatio { get; set; }

        // For single-image exact crop (Optional)
        public bool UseExactCrop { get; set; }
        public uint ExactCropX { get; set; }
        public uint ExactCropY { get; set; }
        public uint ExactCropWidth { get; set; }
        public uint ExactCropHeight { get; set; }

        // Output size options
        public bool IsTargetSizeEnabled { get; set; }
        public long TargetMinFileSizeBytes { get; set; }
        public long TargetMaxFileSizeBytes { get; set; }
    }
}
