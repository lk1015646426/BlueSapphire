using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class DevLogDataService
    {
        private const string FileName = "DevMatrixLog.json";

        public async Task<List<DevLogItem>> LoadLogsAsync()
        {
            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                var file = await localFolder.GetItemAsync(FileName) as StorageFile;
                if (file == null) return new List<DevLogItem>();

                string json = await FileIO.ReadTextAsync(file);
                return JsonSerializer.Deserialize<List<DevLogItem>>(json) ?? new List<DevLogItem>();
            }
            catch
            {
                return new List<DevLogItem>(); // 文件不存在或损坏时返回空列表
            }
        }

        public async Task SaveLogsAsync(List<DevLogItem> logs)
        {
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFile file = await localFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
            string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
            await FileIO.WriteTextAsync(file, json);
        }
    }
}