using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class AgentSkillManager
    {
        private readonly string _configFilePath;
        private readonly IHttpClientFactory _httpClientFactory;
        
        public ObservableCollection<AgentSkillConfig> Skills { get; } = new();

        public AgentSkillManager(IHttpClientFactory httpClientFactory)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueSapphire");
            Directory.CreateDirectory(appData);
            _configFilePath = Path.Combine(appData, "agentskills.json");
            
            _httpClientFactory = httpClientFactory;

            LoadConfig();
        }

        private void LoadConfig()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<AgentSkillConfig>>(json);
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            Skills.Add(item);
                        }
                    }
                }
                catch { }
            }
        }

        public void SaveConfig()
        {
            try
            {
                var list = Skills.ToList();
                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }

        public async Task<bool> AddSkillAsync(string url, bool useDomesticNetwork = false)
        {
            // Auto format Github URLs to Raw URL if pointing to a directory or blob
            string rawUrl = TryConvertToRawGithubUrl(url);

            try
            {
                var client = _httpClientFactory.CreateClient(useDomesticNetwork ? "DeepSeek" : "ProxyTools");
                string markdown = await client.GetStringAsync(rawUrl);
                
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
                
                // Update existing or add new
                var existing = Skills.FirstOrDefault(s => s.Url == url || s.Name == skill.Name);
                if (existing != null)
                {
                    existing.Name = skill.Name;
                    existing.Description = skill.Description;
                    existing.Instructions = skill.Instructions;
                    existing.UseDomesticNetwork = skill.UseDomesticNetwork;
                    existing.AddedAt = DateTime.UtcNow;
                }
                else
                {
                    Skills.Add(skill);
                }
                
                SaveConfig();
                return true;
            }
            catch (Exception ex)
            {
                // Rethrow so the caller can see the actual error message instead of silently returning false
                throw new Exception($"下载技能失败: {ex.Message}", ex);
            }
        }

        private string TryConvertToRawGithubUrl(string url)
        {
            // e.g. https://github.com/KKKKhazix/khazix-skills/tree/main/aihot
            // to https://raw.githubusercontent.com/KKKKhazix/khazix-skills/main/aihot/SKILL.md
            
            if (url.Contains("github.com") && !url.Contains("raw.githubusercontent.com"))
            {
                var match = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/tree/([^/]+)/(.+)");
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
                var blobMatch = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/blob/([^/]+)/(.+)");
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
            var match = Regex.Match(markdown, @"^---\s*\n(.*?)\n---\s*\n(.*)", RegexOptions.Singleline);
            if (match.Success)
            {
                string frontmatter = match.Groups[1].Value;
                config.Instructions = match.Groups[2].Value.Trim();

                var nameMatch = Regex.Match(frontmatter, @"name:\s*(.+)");
                if (nameMatch.Success) config.Name = nameMatch.Groups[1].Value.Trim(' ', '"', '\'');

                var descMatch = Regex.Match(frontmatter, @"description:\s*(.+)");
                if (descMatch.Success) config.Description = descMatch.Groups[1].Value.Trim(' ', '"', '\'');
            }
            else
            {
                // No frontmatter
                config.Instructions = markdown.Trim();
                config.Name = "Custom Agent Skill";
            }

            return config;
        }
    }
}
