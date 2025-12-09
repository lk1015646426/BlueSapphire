using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Helpers
{
    public static class FileHelper
    {
        // 计算文件的 MD5 哈希值
        public static async Task<string> ComputeMD5Async(StorageFile file)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using (var stream = await file.OpenStreamForReadAsync())
                    {
                        using (var md5 = MD5.Create())
                        {
                            var hash = md5.ComputeHash(stream);
                            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        }
                    }
                }
                catch
                {
                    return string.Empty; // 如果文件被占用或无法读取，返回空
                }
            });
        }
    }
}