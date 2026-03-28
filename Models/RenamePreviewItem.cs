using Windows.Storage;

namespace BlueSapphire.Models
{
    public class RenamePreviewItem
    {
        public required StorageFile File { get; set; }
        public required string OriginalPath { get; set; }
        public required string OriginalName { get; set; }
        public required string NewName { get; set; }
        // 预览显示的文字，例如 "IMG001.jpg -> 20231201_120000_01.jpg"
        public string DisplayText => $"{OriginalName} \u2192 {NewName}";
    }
}
