using System.Collections.Generic;
using System.Threading.Tasks;
using BlueSapphire.Models;
using Windows.Storage;

namespace BlueSapphire.Interfaces
{
    public interface IMediaViewInteraction
    {
        Task ShowTipAsync(string message);
        Task<bool> ShowDeleteConfirmationAsync(int count);
        Task<StorageFolder?> PickFolderAsync();
        Task<IReadOnlyList<StorageFile>> PickFilesAsync();
        Task SelectItemsByPathsAsync(IReadOnlyCollection<string> paths);
        Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> duplicates);
        Task<bool> ShowRenamePreviewAsync(List<RenamePreviewItem> items, int skippedCount);
        Task<string?> ShowInputPromptAsync(string title, string message, string defaultText);
        Task<FormatConvertOptions?> ShowFormatConvertDialogAsync(IReadOnlyList<string> sourceFiles);
        Task<AdvancedEditOptions?> ShowAdvancedEditorDialogAsync(IList<string> previewImagePaths);
        Task<EnhanceOptions?> ShowEnhanceDialogAsync(string? previewImagePath);
    }
}
