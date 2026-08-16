using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using BlueSapphire.Models;
using BlueSapphire.Helpers;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services.Skills
{
    public class AgentSkillManager
    {
        private const int MaxSkillBytes = 512 * 1024;
        private const int MaxSkills = 32;
        private const long MaxConfigBytes = 20L * 1024 * 1024;
        private readonly string _configFilePath;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AgentSkillManager>? _logger;

        public ObservableCollection<AgentSkillConfig> Skills { get; } = new();

        public AgentSkillManager(
            IHttpClientFactory httpClientFactory,
            ILogger<AgentSkillManager>? logger = null)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueSapphire");
            Directory.CreateDirectory(appData);
            _configFilePath = Path.Combine(appData, "agentskills.json");

            _httpClientFactory = httpClientFactory;
            _logger = logger;

            LoadConfig();
        }

        private void LoadConfig()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    if (new FileInfo(_configFilePath).Length is <= 0 or > MaxConfigBytes)
                    {
                        return;
                    }
                    string json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<AgentSkillConfig>>(json);
                    if (list != null)
                    {
                        foreach (var item in list.Take(MaxSkills))
                        {
                            NormalizeLoadedSkill(item);
                            if (string.IsNullOrWhiteSpace(item.Instructions)) continue;
                            Skills.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 配置损坏或不可读时技能列表为空，用户需要在设置页重新导入，必须留痕。
                    _logger?.LogWarning(ex, "Agent Skill 配置加载失败，已按空技能列表启动：{ConfigPath}", _configFilePath);
                }
            }
        }

        public void SaveConfig()
        {
            try
            {
                var list = Skills.ToList();
                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                string temporaryPath = _configFilePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _configFilePath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save Agent skills: {ex.Message}");
            }
        }

        public void RemoveSkill(string id)
        {
            AgentSkillConfig? skill = Skills.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (skill == null)
            {
                return;
            }

            Skills.Remove(skill);
            SaveConfig();
        }

        public async Task<bool> AddSkillAsync(
            string url,
            bool useDomesticNetwork = false,
            CancellationToken cancellationToken = default)
        {
            // Auto format Github URLs to Raw URL if pointing to a directory or blob
            url = (url ?? string.Empty).Trim();
            string rawUrl = TryConvertToRawGithubUrl(url);
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? skillUri) ||
                skillUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Agent 技能地址必须使用 HTTPS。");
            }

            try
            {
                var client = _httpClientFactory.CreateClient(useDomesticNetwork ? "DeepSeek" : "ProxyTools");
                using HttpResponseMessage response = await NetworkSafety.GetFollowingSafeRedirectsAsync(
                    client,
                    skillUri,
                    requireHttps: true,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaxSkillBytes)
                {
                    throw new InvalidOperationException("技能文件超过 512 KB 限制。");
                }

                string markdown = await NetworkSafety.ReadContentAsStringAsync(
                    response.Content,
                    MaxSkillBytes,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidOperationException("技能文件为空。");
                }
                
                // If the response is suspiciously like JSON instead of Markdown, we should probably fail.
                // Or maybe let it parse and have weird instructions.
                if (markdown.TrimStart().StartsWith("{") || markdown.TrimStart().StartsWith("["))
                {
                    return false; // Looks like JSON, probably should be handled by WebSkillManager
                }
                
                // Parse frontmatter
                var skill = ParseSkillMarkdown(markdown);
                skill.Url = url;
                skill.UseDomesticNetwork = useDomesticNetwork;
                skill.IsEnabled = false;
                skill.IsTrusted = false;
                
                // Update existing or add new
                var existing = Skills.FirstOrDefault(s =>
                    string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, skill.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = skill.Name;
                    existing.Description = skill.Description;
                    existing.Instructions = skill.Instructions;
                    existing.UseDomesticNetwork = skill.UseDomesticNetwork;
                    existing.IsEnabled = false;
                    existing.IsTrusted = false;
                    existing.AddedAt = DateTime.UtcNow;
                }
                else
                {
                    if (Skills.Count >= MaxSkills)
                    {
                        throw new InvalidOperationException($"最多只能保存 {MaxSkills} 个 Agent 技能。");
                    }
                    Skills.Add(skill);
                }
                
                SaveConfig();
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Rethrow so the caller can see the actual error message instead of silently returning false
                throw new Exception($"下载技能失败: {ex.Message}", ex);
            }
        }

        private static string TryConvertToRawGithubUrl(string url)
        {
            // e.g. https://github.com/KKKKhazix/khazix-skills/tree/main/aihot
            // to https://raw.githubusercontent.com/KKKKhazix/khazix-skills/main/aihot/SKILL.md
            
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? inputUri) &&
                inputUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                string cleanUrl = inputUri.GetLeftPart(UriPartial.Path);
                var match = Regex.Match(
                    cleanUrl,
                    @"^https://github\.com/([^/]+)/([^/]+)/tree/([^/]+)/(.+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    string owner = match.Groups[1].Value;
                    string repo = match.Groups[2].Value;
                    string branch = match.Groups[3].Value;
                    string path = match.Groups[4].Value;
                    
                    if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        path = path.TrimEnd('/') + "/SKILL.md";
                    }
                    
                    return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
                }

                // If it's a blob url
                var blobMatch = Regex.Match(
                    cleanUrl,
                    @"^https://github\.com/([^/]+)/([^/]+)/blob/([^/]+)/(.+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (blobMatch.Success)
                {
                    string owner = blobMatch.Groups[1].Value;
                    string repo = blobMatch.Groups[2].Value;
                    string branch = blobMatch.Groups[3].Value;
                    string path = blobMatch.Groups[4].Value;
                    return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
                }
            }

            // Fallback: if not ending in .md, append SKILL.md
            if (!url.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return url.TrimEnd('/') + "/SKILL.md";
            }
            
            return url;
        }

        private AgentSkillConfig ParseSkillMarkdown(string markdown)
        {
            var config = new AgentSkillConfig();
            
            // Extract YAML Frontmatter
            var match = Regex.Match(markdown, @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n(.*)", RegexOptions.Singleline);
            if (match.Success)
            {
                string frontmatter = match.Groups[1].Value;
                config.Instructions = match.Groups[2].Value.Trim();

                var nameMatch = Regex.Match(frontmatter, @"(?m)^name:\s*(.+)$");
                if (nameMatch.Success) config.Name = nameMatch.Groups[1].Value.Trim(' ', '"', '\'');

                var descMatch = Regex.Match(frontmatter, @"(?m)^description:\s*(.+)$");
                if (descMatch.Success) config.Description = descMatch.Groups[1].Value.Trim(' ', '"', '\'');
            }
            else
            {
                // No frontmatter
                config.Instructions = markdown.Trim();
                config.Name = "Custom Agent Skill";
            }

            config.Name = string.IsNullOrWhiteSpace(config.Name)
                ? "未命名 Agent 技能"
                : config.Name[..Math.Min(config.Name.Length, 100)];
            config.Description = (config.Description ?? string.Empty)
                [..Math.Min((config.Description ?? string.Empty).Length, 500)];
            if (string.IsNullOrWhiteSpace(config.Instructions))
            {
                throw new InvalidOperationException("技能文件没有可用的指令正文。");
            }
            return config;
        }

        private static void NormalizeLoadedSkill(AgentSkillConfig skill)
        {
            if (string.IsNullOrWhiteSpace(skill.Id) || skill.Id.Length > 100)
            {
                skill.Id = Guid.NewGuid().ToString("N");
            }
            skill.Name = string.IsNullOrWhiteSpace(skill.Name)
                ? "未命名 Agent 技能"
                : skill.Name[..Math.Min(skill.Name.Length, 100)];
            skill.Description = (skill.Description ?? string.Empty)
                [..Math.Min((skill.Description ?? string.Empty).Length, 500)];
            skill.Url = (skill.Url ?? string.Empty)[..Math.Min((skill.Url ?? string.Empty).Length, 2048)];
            skill.Instructions = (skill.Instructions ?? string.Empty)
                [..Math.Min((skill.Instructions ?? string.Empty).Length, MaxSkillBytes)];
            if (!skill.IsTrusted)
            {
                skill.IsEnabled = false;
            }
        }
    }
}
