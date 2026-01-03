using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using BlueSapphire.Models; // 确保引用了 Models

namespace BlueSapphire.Interfaces
{
    public interface IMediaViewInteraction
    {
        Task ShowTipAsync(string message);
        Task<bool> ShowDeleteConfirmationAsync(int count);
        Task<StorageFolder?> PickFolderAsync();
        Task<List<StorageFile>> ShowDuplicateResultsAsync(List<List<StorageFile>> dupes);

        // [新增] 显示重命名预览弹窗
        // 返回 true 表示用户确认执行，false 表示取消
        Task<bool> ShowRenamePreviewAsync(List<RenamePreviewItem> items, int skippedCount);
    }
}