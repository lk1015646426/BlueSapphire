using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class DevLogDataService
    {
        private const string FileName = "DevMatrixLog.json";

        // 确保读写操作不冲突
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        // 【核心修复】适配未打包 (Unpackaged) 的 WinUI 3 应用
        // 彻底弃用会报错的 ApplicationData.Current，改用系统标准的 LocalAppData 目录
        private string FilePath
        {
            get
            {
                // 这将获取到 C:\Users\你的用户名\AppData\Local
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // 为你的工具箱创建一个专属文件夹
                string appFolder = Path.Combine(localAppData, "BlueSapphire");

                if (!Directory.Exists(appFolder))
                {
                    Directory.CreateDirectory(appFolder);
                }

                return Path.Combine(appFolder, FileName);
            }
        }

        public async Task<List<DevLogItem>> LoadLogsAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
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