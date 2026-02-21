using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class DevLogDataService
    {
        private const string FileName = "DevMatrixLog.json";

        // 确保读写操作不冲突
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        // 【关键修复】使用动态属性（懒加载）获取路径
        // 这样可以避免在程序刚启动时（构造函数中）过早调用 ApplicationData.Current 导致崩溃
        private string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);

        public async Task<List<DevLogItem>> LoadLogsAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                // 每次操作时才获取具体路径
                string targetPath = FilePath;

                if (!File.Exists(targetPath))
                {
                    return new List<DevLogItem>();
                }

                string json = await File.ReadAllTextAsync(targetPath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<DevLogItem>();
                }

                return JsonSerializer.Deserialize<List<DevLogItem>>(json) ?? new List<DevLogItem>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取日志报错: {ex.Message}");
                return new List<DevLogItem>();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveLogsAsync(List<DevLogItem> logs)
        {
            await _fileLock.WaitAsync();
            try
            {
                logs ??= new List<DevLogItem>();
                string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });

                string targetPath = FilePath;
                string tempFilePath = targetPath + ".tmp";

                // 原子化写入：先写临时文件
                await File.WriteAllTextAsync(tempFilePath, json);

                // 写入成功后瞬间替换原文件
                File.Move(tempFilePath, targetPath, true);

                System.Diagnostics.Debug.WriteLine($"已成功保存记录。当前记录数：{logs.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存日志报错: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }
}