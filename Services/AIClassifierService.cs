using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class AIClassifierService
    {
        private readonly DeepSeekAIService _aiService;
        private readonly AIPrivacyService _privacyService;
        private readonly Dictionary<string, AIClassificationResult> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _semaphore = new(3, 3); // 限制并发数为 3

        public AIClassifierService(DeepSeekAIService aiService, AIPrivacyService? privacyService = null)
        {
            _aiService = aiService;
            _privacyService = privacyService ?? new AIPrivacyService();
        }

        public async Task<AIClassificationResult?> ClassifyDirectoryAsync(string path, long sizeBytes, CancellationToken cancellationToken = default)
        {
            string normalized = CleanerPathSafety.NormalizePath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

            if (_cache.TryGetValue(normalized, out var cached))
                return cached;

            string sizeText = CleanerSizeFormatter.Format(sizeBytes);
            string folderName = System.IO.Path.GetFileName(path);
            string pathForModel = BuildPathForRemoteModel(normalized);
            string folderNameForModel = IsPersonalUserPath(normalized) ? "<用户目录名称已隐藏>" : folderName;

            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "system",
                    Content = @"你是一个 Windows 磁盘清理分类助手。用户会提供一个目录路径及其占用空间大小，你需要判断它是什么、能否安全清理。

请严格返回以下 JSON 格式（不要额外文字）：
{
  ""category"": ""类别英文标识"",
  ""riskLevel"": ""Low|Medium|High"",
  ""safeToClean"": true/false,
  ""name"": ""中文显示名称"",
  ""description"": ""一句话说明这是什么"",
  ""cleanReason"": ""为什么可以或不可以清理""
}

类别可选值：dev_cache(开发者缓存), system_temp(系统临时), app_cache(应用缓存), app_logs(日志), media_cache(媒体缓存), user_data(用户数据), unknown(未知)

判断原则：
- pip/npm/nuget/cargo/gradle/maven 等包管理器缓存 → dev_cache, Low, 可清理
- Windows Temp/临时文件 → system_temp, Low, 可清理
- 浏览器缓存 → app_cache, Low, 可清理
- IDE/编辑器缓存 → app_cache, Low, 可清理
- 崩溃转储/错误报告 → app_logs, Medium, 可清理
- 微信/QQ 等聊天记录 → user_data, High, 不可清理
- 源码/项目/文档 → user_data, High, 不可清理
- 游戏/应用主体文件 → user_data, High, 不可清理"
                },
                new()
                {
                    Role = "user",
                    Content = $"路径: {pathForModel}\n文件夹名: {folderNameForModel}\n占用空间: {sizeText}\n\n请提供辅助分类。分类结果只用于解释，不会直接授权删除。"
                }
            };

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var response = await _aiService.SendChatAsync(messages, null, linkedCts.Token);
                string content = response.Content?.Trim() ?? "";

                // Extract JSON from response (in case AI wraps it in markdown)
                int jsonStart = content.IndexOf('{');
                int jsonEnd = content.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    content = content[jsonStart..(jsonEnd + 1)];
                }

                var result = JsonSerializer.Deserialize<AIClassificationResult>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result != null)
                {
                    result.Path = normalized;
                    _cache[normalized] = result;
                    return result;
                }
            }
            catch
            {
                // AI classification failed, return null → fall back to ViewOnly
            }
            finally
            {
                _semaphore.Release();
            }

            return null;
        }

        private string BuildPathForRemoteModel(string normalizedPath)
        {
            if (IsPersonalUserPath(normalizedPath))
            {
                string root = System.IO.Path.GetPathRoot(normalizedPath) ?? string.Empty;
                return $"{root}Users\\<用户>\\<个人内容路径已隐藏>";
            }

            return _privacyService.RedactForRemoteModel(normalizedPath);
        }

        private static bool IsPersonalUserPath(string normalizedPath)
        {
            string userProfile = CleanerPathSafety.NormalizePath(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            string localAppData = CleanerPathSafety.NormalizePath(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            string roamingAppData = CleanerPathSafety.NormalizePath(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

            return CleanerPathSafety.StartsWithPathBoundary(normalizedPath, userProfile) &&
                   !CleanerPathSafety.StartsWithPathBoundary(normalizedPath, localAppData) &&
                   !CleanerPathSafety.StartsWithPathBoundary(normalizedPath, roamingAppData);
        }

        public void ClearCache()
        {
            _cache.Clear();
        }
    }

    public sealed class AIClassificationResult
    {
        public string Path { get; set; } = "";
        public string Category { get; set; } = "unknown";
        public string RiskLevel { get; set; } = "High";
        public bool SafeToClean { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string CleanReason { get; set; } = "";
    }
}
