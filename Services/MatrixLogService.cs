using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Services
{
    /// <summary>
    /// 极客专属：基于 Channel 的高性能异步日志服务
    /// 保证 0 锁争用、0 UI 线程阻塞
    /// </summary>
    public static class MatrixLogService
    {
        // 创建一个无界通道，作为后台日志队列
        private static readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>();
        private static StorageFile? _logFile;
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // 启动后台消费者，避免在主线程或调用线程上阻塞日志落盘
            _ = Task.Run(ProcessLogsAsync);
        }

        public static void LogError(string context, Exception ex)
        {
            // 极速压入队列，立即返回，绝不卡顿调用方
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [{context}] {ex.Message}\n{ex.StackTrace}";
            _logChannel.Writer.TryWrite(msg);
        }

        public static void LogInfo(string context, string message)
        {
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] [{context}] {message}";
            _logChannel.Writer.TryWrite(msg);
        }

        private static async Task ProcessLogsAsync()
        {
            try
            {
                // 将日志文件存放在应用的 LocalAppData 目录下，符合沙盒/无包应用的规范
                var localFolder = ApplicationData.Current.LocalFolder;
                _logFile = await localFolder.CreateFileAsync("Matrix_CrashLog.txt", CreationCollisionOption.OpenIfExists);

                // 异步等待队列中的数据，没有日志时完全休眠，不消耗 CPU
                await foreach (var logMsg in _logChannel.Reader.ReadAllAsync())
                {
                    // 追加写入
                    await File.AppendAllTextAsync(_logFile.Path, logMsg + Environment.NewLine);
                }
            }
            catch
            {
                // 日志系统的最高原则：自己崩溃了绝对不能带着主程序一起死
                // 这里静默吞掉底层 IO 异常
            }
        }
    }
}
