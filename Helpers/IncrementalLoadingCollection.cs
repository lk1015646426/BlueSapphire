using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace BlueSapphire.Helpers
{
    /// <summary>
    /// 一个支持增量加载（无限滚动）的通用集合类。
    /// </summary>
    /// <typeparam name="T">集合中存放的数据类型 (例如 ImageItem)</typeparam>
    public class IncrementalLoadingCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
    {
        // 核心委托：由外部提供“如何获取下一页数据”的逻辑
        private readonly Func<CancellationToken, uint, Task<IEnumerable<T>>> _loadMoreItemsFunc;

        // 标记：是否还有更多数据？
        private bool _hasMoreItems = true;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="loadMoreItemsFunc">一个函数，接收 (取消令牌, 请求数量)，返回一批新数据</param>
        public IncrementalLoadingCollection(Func<CancellationToken, uint, Task<IEnumerable<T>>> loadMoreItemsFunc)
        {
            _loadMoreItemsFunc = loadMoreItemsFunc;
        }

        // 接口属性：告诉 UI 控件是否应该继续触发加载
        public bool HasMoreItems => _hasMoreItems;

        // 接口方法：UI 控件滚动到底部时会自动调用此方法
        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return AsyncInfo.Run(async cancellationToken =>
            {
                try
                {
                    // 1. 调用外部逻辑获取新数据
                    var newItems = await _loadMoreItemsFunc(cancellationToken, count);

                    // 2. 这里的 count 是 UI 建议的数量，实际返回可能少于它
                    uint addedCount = 0;

                    if (newItems != null && newItems.Any())
                    {
                        foreach (var item in newItems)
                        {
                            this.Add(item); // 将新数据加入到自身 (ObservableCollection)
                            addedCount++;
                        }
                    }
                    else
                    {
                        // 如果返回空，说明没数据了
                        _hasMoreItems = false;
                    }

                    // 3. 返回本次加载的数量
                    return new LoadMoreItemsResult { Count = addedCount };
                }
                catch (Exception)
                {
                    // 发生错误时停止加载，避免无限重试导致崩溃
                    _hasMoreItems = false;
                    throw;
                }
            });
        }
    }
}