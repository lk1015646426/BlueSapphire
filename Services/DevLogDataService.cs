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
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        private string DevFilePath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appFolder = Path.Combine(localAppData, "BlueSapphire");
                if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, FileName);
            }
        }

        private string PackagedFilePath
        {
            get
            {
                return Path.Combine(AppContext.BaseDirectory, "Assets", FileName);
            }
        }

        // 【极度严苛的权限判定】
        private bool IsPackaged()
        {
#if DEBUG
            // 只有在 Visual Studio 中按 F5 调试运行 (Debug 模式) 时，才视为未打包 (允许读写)
            return false;
#else
            // 一旦通过 Builder 以 Release 模式打包发布，强制判定为已打包，全面锁死为只读！
            return true;
#endif
        }

        public async Task<List<DevLogItem>> LoadLogsAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                string targetPath = IsPackaged() ? PackagedFilePath : DevFilePath;

                if (!File.Exists(targetPath)) return new List<DevLogItem>();

                string json = await File.ReadAllTextAsync(targetPath);
                if (string.IsNullOrWhiteSpace(json)) return new List<DevLogItem>();

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
            // 发布版本直接拦截，拒绝落盘
            if (IsPackaged()) return;

            await _fileLock.WaitAsync();
            try
            {
                logs ??= new List<DevLogItem>();
                string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });

                string targetPath = DevFilePath;
                string tempFilePath = targetPath + ".tmp";

                await File.WriteAllTextAsync(tempFilePath, json);
                File.Move(tempFilePath, targetPath, true);
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