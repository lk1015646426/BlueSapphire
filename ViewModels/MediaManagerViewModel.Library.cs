using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace BlueSapphire.ViewModels
{
    // 图片库加载与视图刷新分部：目录/文件导入、缓存快照、过滤排序、增量加载与幽灵文件清理。
    public partial class MediaManagerViewModel
    {
        private async Task LoadFolderContentAsync(StorageFolder folder)
        {
            SetBusy(true, "正在扫描文件夹...");

            try
            {
                var files = await folder.CreateFileQueryWithOptions(MediaFileCatalog.CreateImageQueryOptions()).GetFilesAsync();
                await LoadFilesAsync(files, folder.Path);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                await _view.ShowTipAsync($"加载文件夹失败: {ex.Message}");
            }
        }

        private async Task LoadFilesAsync(IReadOnlyList<StorageFile> files, string displayPath)
        {
            SetBusy(true, "正在读取图片信息...");

            Images = null;
            lock (_cachedAllItems)
            {
                _cachedAllItems.Clear();
            }

            PathText = displayPath;
            CountText = "0";
            HasImages = false;
            IsEmptyStateVisible = false;

            if (files.Count == 0)
            {
                IsEmptyStateVisible = true;
                SetBusy(false);
                return;
            }

            var concurrentItems = new ConcurrentBag<ImageItem>();
            int processedCount = 0;
            int totalFiles = files.Count;

            RunOnUi(() =>
            {
                ProgressMax = totalFiles;
                ProgressValue = 0;
            });

            bool isLargeDataset = totalFiles > 500;
            int uiStep = totalFiles > 5000 ? 200 : (totalFiles > 1000 ? 50 : 10);

            try
            {
                await Parallel.ForEachAsync(
                    files,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount * 2) },
                    async (file, _) =>
                {
                try
                {
                    var item = await CreateImageItemAsync(file, !isLargeDataset);
                    concurrentItems.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Load_Image ({FileName})", file.Name);
                }
                finally
                {
                    int current = Interlocked.Increment(ref processedCount);
                    if (current % uiStep == 0 || current == totalFiles)
                    {
                        RunOnUi(() =>
                        {
                            ProgressValue = current;
                            StatusDetailText = $"{current} / {totalFiles}";
                        });
                    }
                }
                });

                lock (_cachedAllItems)
                {
                    _cachedAllItems.AddRange(concurrentItems);
                }

                await RefreshViewFromCacheAsync();

                int skippedCount = files.Count - concurrentItems.Count;
                if (skippedCount > 0)
                {
                    await _view.ShowTipAsync($"加载完成，但有 {skippedCount} 张图片因无法读取或损坏而被跳过。");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Load_Files_Critical");
                await _view.ShowTipAsync($"读取失败: {ex.Message}");
                IsEmptyStateVisible = true;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RefreshViewFromCacheAsync(bool showBusy = true)
        {
            long refreshVersion = Interlocked.Increment(ref _viewRefreshVersion);
            if (showBusy)
            {
                SetBusy(true, "正在重新排序...", 0, 100);
            }

            List<ImageItem> snapshot;
            lock (_cachedAllItems)
            {
                snapshot = _cachedAllItems.ToList();
            }

            string searchText = SearchText.Trim();
            string tagFilterMode = TagFilterMode;
            var sortedList = await Task.Run(() =>
            {
                IEnumerable<ImageItem> filtered = snapshot;
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filtered = filtered.Where(item =>
                        (item.FileName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (item.ImagePath?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        item.CustomTags.Any(tag => tag.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
                        (item.ImageFormat?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                filtered = tagFilterMode switch
                {
                    "Tagged" => filtered.Where(item => item.HasCustomTags),
                    "Untagged" => filtered.Where(item => !item.HasCustomTags),
                    _ => filtered
                };
                List<ImageItem> filteredItems = filtered.ToList();

                return CurrentSortField switch
                {
                    "Date" => IsSortDescending
                        ? filteredItems.OrderByDescending(item => item.DateCreated).ToList()
                        : filteredItems.OrderBy(item => item.DateCreated).ToList(),
                    "Size" => IsSortDescending
                        ? filteredItems.OrderByDescending(item => item.FileSize).ToList()
                        : filteredItems.OrderBy(item => item.FileSize).ToList(),
                    _ => IsSortDescending
                        ? filteredItems.OrderByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                        : filteredItems.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                };
            });

            if (refreshVersion != Interlocked.Read(ref _viewRefreshVersion))
            {
                if (showBusy)
                {
                    RunOnUi(() => SetBusy(false));
                }
                return;
            }

            RunOnUi(() =>
            {
                if (refreshVersion != Interlocked.Read(ref _viewRefreshVersion))
                {
                    return;
                }
                CountText = sortedList.Count == snapshot.Count
                    ? snapshot.Count.ToString()
                    : $"{sortedList.Count} / {snapshot.Count}";
                HasImages = snapshot.Count > 0;
                HasVisibleImages = sortedList.Count > 0;
                IsEmptyStateVisible = snapshot.Count == 0;

                _lastVisibleItems = sortedList;

                int offset = 0;
                Images = new IncrementalLoadingCollection<ImageItem>((ct, count) =>
                {
                    int takeCount = Math.Min((int)count, sortedList.Count - offset);
                    if (takeCount <= 0)
                    {
                        return Task.FromResult<IEnumerable<ImageItem>>(Array.Empty<ImageItem>());
                    }

                    var batch = sortedList.GetRange(offset, takeCount);
                    offset += takeCount;

                    // 后台异步填充当前分页可视窗口内图片的 EXIF 元数据（高阶分辨率与色彩位深）
                    _ = Task.Run(async () =>
                    {
                        // 火忘调用：外层必须兜底。CreateLinkedTokenSource 可在所属操作
                        // 的 CTS 已释放时抛 ObjectDisposedException（取消窗口竞态）。
                        try
                        {
                            using CancellationTokenSource linkedCts =
                                CancellationTokenSource.CreateLinkedTokenSource(ct, _metadataCts.Token);
                            CancellationToken metadataToken = linkedCts.Token;
                            foreach (var item in batch)
                            {
                                if (metadataToken.IsCancellationRequested) break;
                                if (item.ImageWidth == 0 && item.ImageHeight == 0)
                                {
                                    try
                                    {
                                        var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
                                        metadataToken.ThrowIfCancellationRequested();
                                        var meta = await _imageMetadataService.TryReadAsync(file);
                                        if (meta != null)
                                        {
                                            RunOnUi(() =>
                                            {
                                                if (metadataToken.IsCancellationRequested) return;
                                                item.ImageWidth = meta.Width;
                                                item.ImageHeight = meta.Height;
                                                item.ImageFormat = meta.FormatName;
                                                item.ImageBitDepth = meta.BitDepth;
                                                item.ImageDateTaken = meta.DateTaken;
                                            });
                                        }
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        break;
                                    }
                                    catch
                                    {
                                        // 单个损坏或不可访问文件不应中止整批元数据读取。
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // 元数据是增强信息，后台填充失败直接放弃本批。
                        }
                    });

                    return Task.FromResult<IEnumerable<ImageItem>>(batch);
                });

                if (showBusy)
                {
                    SetBusy(false);
                }
            });
        }

        private async Task<ImageItem> CreateImageItemAsync(StorageFile file, bool loadMetadata = true)
        {
            ulong fileSize = 0;
            DateTimeOffset dateCreated = file.DateCreated;
            try
            {
                var fi = new System.IO.FileInfo(file.Path);
                if (fi.Exists)
                {
                    fileSize = (ulong)fi.Length;
                    dateCreated = fi.CreationTimeUtc;
                }
                else
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    fileSize = properties.Size;
                }
            }
            catch
            {
                // 属性读取失败保持 0，不影响建库主流程。
                try { var properties = await file.GetBasicPropertiesAsync(); fileSize = properties.Size; } catch { }
            }

            var item = new ImageItem
            {
                FileName = file.Name,
                ImagePath = file.Path,
                DateCreated = dateCreated,
                FileSize = fileSize,
                CustomTags = await _mediaTagService.GetTagsAsync(file.Path)
            };

            if (loadMetadata)
            {
                var metadata = await _imageMetadataService.TryReadAsync(file);
                if (metadata != null)
                {
                    item.ImageWidth = metadata.Width;
                    item.ImageHeight = metadata.Height;
                    item.ImageFormat = metadata.FormatName;
                    item.ImageBitDepth = metadata.BitDepth;
                    item.ImageDateTaken = metadata.DateTaken;
                }
            }

            return item;
        }

        private async Task TrackOutputPathAsync(string outputPath)
        {
            if (!System.IO.File.Exists(outputPath) || !MediaFileCatalog.IsImage(outputPath) || !IsPathUnderCurrentFolder(outputPath))
            {
                return;
            }

            await TrackFileInCacheAsync(outputPath);
        }

        private async Task TrackFileInCacheAsync(string filePath)
        {
            var file = await TryGetStorageFileAsync(filePath);
            if (file == null || !MediaFileCatalog.IsImage(file.Name))
            {
                return;
            }

            var trackedItem = await CreateImageItemAsync(file);
            lock (_cachedAllItems)
            {
                int index = _cachedAllItems.FindIndex(item =>
                    string.Equals(item.ImagePath, file.Path, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    _cachedAllItems[index] = trackedItem;
                }
                else
                {
                    _cachedAllItems.Add(trackedItem);
                }
            }
        }

        private async Task RemoveGhostFilesAsync(IEnumerable<string> ghostPaths, bool refreshView = true)
        {
            var pathSet = ghostPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (pathSet.Count == 0)
            {
                return;
            }

            lock (_cachedAllItems)
            {
                _cachedAllItems.RemoveAll(item =>
                    !string.IsNullOrWhiteSpace(item.ImagePath) &&
                    pathSet.Contains(item.ImagePath));
            }

            await _mediaTagService.RemoveTagsAsync(pathSet);
            if (refreshView)
            {
                await RefreshViewFromCacheAsync();
            }
        }

        private bool IsPathUnderCurrentFolder(string filePath)
        {
            if (_currentFolder == null || string.IsNullOrWhiteSpace(_currentFolder.Path))
            {
                return false;
            }

            string folderPath = System.IO.Path.GetFullPath(_currentFolder.Path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string candidatePath = System.IO.Path.GetFullPath(filePath);

            return candidatePath.StartsWith(folderPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidatePath, folderPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
