using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public class DevLogDataService
    {
        private const string FileName = "DevMatrixLog.json";
        private static readonly SemaphoreSlim FileLock = new(1, 1);

        public string DataFilePath
        {
            get
            {
                string appFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlueSapphire");

                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, FileName);
            }
        }

        public bool CanWrite => true;

        private string SeedFilePath => Path.Combine(AppContext.BaseDirectory, "Assets", FileName);

        public async Task<List<DevLogItem>> LoadLogsAsync()
        {
            await FileLock.WaitAsync();
            try
            {
                await EnsureSeededAsync();

                if (!File.Exists(DataFilePath))
                {
                    return new List<DevLogItem>();
                }

                string json = await File.ReadAllTextAsync(DataFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<DevLogItem>();
                }

                return JsonSerializer.Deserialize<List<DevLogItem>>(json) ?? new List<DevLogItem>();
            }
            catch (Exception ex)
            {
                MatrixLogService.LogError("DevLog_Load", ex);
                return new List<DevLogItem>();
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task SaveLogsAsync(List<DevLogItem> logs)
        {
            await FileLock.WaitAsync();
            try
            {
                logs ??= new List<DevLogItem>();
                string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });

                string tempFilePath = DataFilePath + ".tmp";
                await File.WriteAllTextAsync(tempFilePath, json);
                File.Move(tempFilePath, DataFilePath, true);
            }
            catch (Exception ex)
            {
                MatrixLogService.LogError("DevLog_Save", ex);
            }
            finally
            {
                FileLock.Release();
            }
        }

        private async Task EnsureSeededAsync()
        {
            if (File.Exists(DataFilePath))
            {
                return;
            }

            if (!File.Exists(SeedFilePath))
            {
                return;
            }

            try
            {
                string seedJson = await File.ReadAllTextAsync(SeedFilePath);
                if (!string.IsNullOrWhiteSpace(seedJson))
                {
                    await File.WriteAllTextAsync(DataFilePath, seedJson);
                }
            }
            catch (Exception ex)
            {
                MatrixLogService.LogError("DevLog_Seed", ex);
            }
        }
    }
}
