using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services.AI
{
    public sealed class AIChatHistoryService
    {
        private const int MaxSavedMessages = 60;
        private const int MaxSavedCharacters = 100_000;
        private const int MaxMessageCharacters = 32_000;
        private const long MaxEncryptedHistoryBytes = 2 * 1024 * 1024;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _historyPath;
        private readonly ILogger<AIChatHistoryService>? _logger;

        public AIChatHistoryService(string? rootPath = null, ILogger<AIChatHistoryService>? logger = null)
        {
            string folder = rootPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueSapphire");
            Directory.CreateDirectory(folder);
            _historyPath = Path.Combine(folder, "ai_chat_history.dat");
            _logger = logger;
        }

        public async Task<IReadOnlyList<ChatMessage>> LoadAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (!File.Exists(_historyPath))
                {
                    return Array.Empty<ChatMessage>();
                }
                if (new FileInfo(_historyPath).Length is <= 0 or > MaxEncryptedHistoryBytes)
                {
                    return Array.Empty<ChatMessage>();
                }

                byte[] protectedBytes = await File.ReadAllBytesAsync(_historyPath);
                byte[] jsonBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser);
                List<ChatMessage> loaded = JsonSerializer.Deserialize<List<ChatMessage>>(jsonBytes)
                                           ?? new List<ChatMessage>();
                return BuildBoundedHistory(loaded);
            }
            catch
            {
                return Array.Empty<ChatMessage>();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(IEnumerable<ChatMessage> messages)
        {
            List<ChatMessage> bounded = BuildBoundedHistory(messages);
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(bounded);
            byte[] protectedBytes = ProtectedData.Protect(
                jsonBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            await _gate.WaitAsync();
            try
            {
                string tempPath = _historyPath + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(tempPath, protectedBytes);
                    File.Move(tempPath, _historyPath, true);
                }
                catch
                {
                    try
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        // 清理半成品文件失败只记日志，原始保存异常仍向上传递。
                        _logger?.LogWarning(cleanupEx, "聊天历史保存失败后清理残留文件失败：{TempPath}", tempPath);
                    }
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ClearAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (File.Exists(_historyPath))
                {
                    File.Delete(_historyPath);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private static List<ChatMessage> BuildBoundedHistory(IEnumerable<ChatMessage> source)
        {
            List<ChatMessage> candidates = source
                .Where(message =>
                    (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(message.Content))
                .TakeLast(MaxSavedMessages)
                .Select(message =>
                {
                    string content = message.Content!;
                    return new ChatMessage
                    {
                        Role = (message.Role ?? "assistant").ToLowerInvariant(),
                        Content = content[..Math.Min(content.Length, MaxMessageCharacters)]
                    };
                })
                .ToList();

            int characters = candidates.Sum(message => message.Content?.Length ?? 0);
            while (candidates.Count > 0 && characters > MaxSavedCharacters)
            {
                characters -= candidates[0].Content?.Length ?? 0;
                candidates.RemoveAt(0);
            }

            while (candidates.Count > 0 &&
                   !string.Equals(candidates[0].Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                candidates.RemoveAt(0);
            }

            return candidates;
        }
    }
}
