using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using BlueSapphire.Models;

namespace BlueSapphire.Interfaces
{
    public interface IMediaViewInteraction
    {
        Task ShowTipAsync(string message);
        Task<bool> ShowDeleteConfirmationAsync(int count);
        Task<StorageFolder?> PickFolderAsync();
        Task<StorageFile?> PickImageFileAsync();
        Task<StorageFile?> PickCsvFileAsync();
        Task<StorageFile?> PickLyricsFileAsync();
        Task<StorageFile?> PickPlaylistFileAsync();
        Task SelectItemsByPathsAsync(IReadOnlyCollection<string> paths);
        Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> dupes);
        Task ShowDocumentConversionResultsAsync(DocumentConversionBatchReport report);
        Task<DocumentConversionBatchReport?> ShowDocumentTaskHistoryAsync(IReadOnlyList<DocumentConversionBatchReport> reports);
        Task<AudioTrimRequest?> ShowAudioTrimDialogAsync(string fileName, TimeSpan? duration, bool isBatch = false);
        Task<AudioTagEditRequest?> ShowAudioTagEditDialogAsync(AudioTagEditSeed seed);
        Task<bool> ShowRenamePreviewAsync(List<RenamePreviewItem> items, int skippedCount);
        Task<string?> ShowInputPromptAsync(string title, string message, string defaultText);
    }
}
