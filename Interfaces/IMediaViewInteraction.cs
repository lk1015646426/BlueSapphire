using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Interfaces
{
    /// <summary>
    /// 定义 ViewModel 与 View 交互的契约 (主要用于弹窗)
    /// </summary>
    public interface IMediaViewInteraction
    {
        Task ShowTipAsync(string message);
        Task<bool> ShowDeleteConfirmationAsync(int count);
        Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> duplicateGroups);
        Task<StorageFolder?> PickFolderAsync();
    }
}