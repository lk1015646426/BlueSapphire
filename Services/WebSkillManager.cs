using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class WebSkillManager
    {
        private readonly string _configFilePath;
        private readonly IHttpClientFactory _httpClientFactory;
        
        public ObservableCollection<WebSkillConfig> Skills { get; } = new();

        // Dictionary to store generated tools
        // Key is ToolName (e.g. skill__<skillId>__<operationId>)
        private readonly Dictionary<string, ChatTool> _loadedTools = new();
        
        // Dictionary to store raw path and method info to execute requests later
        private readonly Dictionary<string, SkillEndpointInfo> _toolEndpoints = new();

        public WebSkillManager(IHttpClientFactory httpClientFactory)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueSapphire");
            Directory.CreateDirectory(appData);
            _configFilePath = Path.Combine(appData, "webskills.json");
            
            _httpClientFactory = httpClientFactory;

            LoadConfig();
            
            _ = RefreshAllSkillsAsync();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<WebSkillConfig>>(json) ?? new List<WebSkillConfig>();
                    foreach (var s in list)
                    {
                        s.StatusText = "等待加载";
                        s.StatusColor = "#A0FFFFFF";
                        s.IsLoaded = false;
                        Skills.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load web skills config: {ex.Message}");
            }
        }

        public void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(Skills.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save web skills config: {ex.Message}");
            }
        }

        public async Task<(WebSkillConfig? Skill, string ErrorMessage)> AddSkillAsync(string url, bool useDomesticNetwork = false)
        {
            var skill = new WebSkillConfig { Url = url, UseDomesticNetwork = useDomesticNetwork };
            var (success, error) = await RefreshSkillAsync(skill);
            if (success)
            {
                Skills.Add(skill);
                SaveConfig();
                return (skill, string.Empty);
            }
            return (null, error);
        }

        public void RemoveSkillAsync(string id)
        {
            var skill = Skills.FirstOrDefault(x => x.Id == id);
            if (skill != null)
            {
                Skills.Remove(skill);
                SaveConfig();
                
                // Remove loaded tools
                var keysToRemove = _loadedTools.Keys.Where(k => k.StartsWith($"skill__{id}__")).ToList();
                foreach(var k in keysToRemove)
                {
                    _loadedTools.Remove(k);
                    _toolEndpoints.Remove(k);
                }
            }
        }

        public async Task RefreshAllSkillsAsync()
        {
            var tasks = Skills.Select(skill => RefreshSkillAsync(skill)).ToList();
            await Task.WhenAll(tasks);
        }

        private async Task<(bool Success, string ErrorMessage)> RefreshSkillAsync(WebSkillConfig skill)
        {
            try
            {
                skill.StatusText = "正在解析...";
                skill.StatusColor = "#FFFFBB00";

                string json = "";
                JsonDocument? doc = null;
                try
                {
                    var client = _httpClientFactory.CreateClient(skill.UseDomesticNetwork ? "DeepSeek" : "ProxyTools");
                    json = await client.GetStringAsync(skill.Url);
                    doc = JsonDocument.Parse(json);
                }
                catch (JsonException ex)
                {
                    // Catch JSON Parse exception to give a meaningful error
                    throw new Exception($"解析JSON配置失败: {ex.Message}。请确保网址返回的是标准的 OpenAPI JSON 格式。");
                }
                catch (Exception ex) when (doc == null)
                {
                    // Fallback to append openapi.json if the user just provided a base URL
                    if (!skill.Url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        try 
                        {
                            string fallbackUrl = skill.Url.TrimEnd('/') + "/openapi.json";
                            var client = _httpClientFactory.CreateClient(skill.UseDomesticNetwork ? "DeepSeek" : "ProxyTools");
                            json = await client.GetStringAsync(fallbackUrl);
                            doc = JsonDocument.Parse(json);
                            skill.Url = fallbackUrl; // Update to the correct URL
                        } 
                        catch (Exception innerEx) 
                        {
                            throw new Exception($"网络请求或解析 fallback URL 失败: {innerEx.Message} (原始错误: {ex.Message})");
                        }
                    }
                    else
                    {
                        throw new Exception($"网络请求或解析失败: {ex.Message}");
                    }
                }
                
                using var docScope = doc!;
                var root = doc!.RootElement;

                if (root.TryGetProperty("info", out var infoNode))
                {
                    if (infoNode.TryGetProperty("title", out var titleNode))
                    {
                        skill.Name = titleNode.GetString() ?? skill.Name;
                    }
                }

                string baseUrl = "";
                if (root.TryGetProperty("servers", out var serversNode) && serversNode.ValueKind == JsonValueKind.Array && serversNode.GetArrayLength() > 0)
                {
                    var firstServer = serversNode[0];
                    if (firstServer.TryGetProperty("url", out var urlNode))
                    {
                        baseUrl = urlNode.GetString() ?? "";
                    }
                }

                if (string.IsNullOrEmpty(baseUrl))
                {
                    // Fallback to extract base URL from the spec URL
                    var uri = new Uri(skill.Url);
                    baseUrl = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}";
                }

                if (root.TryGetProperty("paths", out var pathsNode))
                {
                    foreach (var path in pathsNode.EnumerateObject())
                    {
                        string pathStr = path.Name;
                        foreach (var method in path.Value.EnumerateObject())
                        {
                            string methodStr = method.Name.ToLower();
                            if (methodStr != "get" && methodStr != "post") continue;

                            string operationId = method.Value.TryGetProperty("operationId", out var opNode) ? opNode.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                            string description = method.Value.TryGetProperty("description", out var descNode) ? descNode.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(description))
                            {
                                description = method.Value.TryGetProperty("summary", out var sumNode) ? sumNode.GetString() ?? "No description" : "No description";
                            }

                            string toolName = $"skill__{skill.Id.Replace("-", "")}__{operationId}";
                            
                            // Parse parameters
                            var propertiesDict = new Dictionary<string, object>();
                            var requiredList = new List<string>();
                            var endpointInfo = new SkillEndpointInfo
                            {
                                BaseUrl = baseUrl,
                                Path = pathStr,
                                Method = methodStr
                            };

                            if (method.Value.TryGetProperty("parameters", out var paramsNode) && paramsNode.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var param in paramsNode.EnumerateArray())
                                {
                                    string paramName = param.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                    if (string.IsNullOrEmpty(paramName)) continue;
                                    
                                    string paramIn = param.TryGetProperty("in", out var inProp) ? inProp.GetString() ?? "" : "";
                                    if (string.Equals(paramIn, "query", StringComparison.OrdinalIgnoreCase))
                                    {
                                        endpointInfo.QueryParamNames.Add(paramName);
                                    }

                                    bool isRequired = param.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True || r.ValueKind == JsonValueKind.True;
                                    if (isRequired) requiredList.Add(paramName);

                                    string paramDesc = param.TryGetProperty("description", out var pd) ? pd.GetString() ?? "" : "";
                                    string paramType = "string";

                                    if (param.TryGetProperty("schema", out var paramSchemaNode) && paramSchemaNode.TryGetProperty("type", out var t))
                                    {
                                        paramType = t.GetString() ?? "string";
                                    }

                                    propertiesDict[paramName] = new
                                    {
                                        type = paramType,
                                        description = paramDesc
                                    };
                                }
                            }

                            if (method.Value.TryGetProperty("requestBody", out var rbNode) &&
                                rbNode.TryGetProperty("content", out var contentNode) &&
                                contentNode.TryGetProperty("application/json", out var jsonNode) &&
                                jsonNode.TryGetProperty("schema", out var schemaNode) &&
                                schemaNode.TryGetProperty("properties", out var propsNode) && propsNode.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in propsNode.EnumerateObject())
                                {
                                    string paramName = prop.Name;
                                    string paramDesc = prop.Value.TryGetProperty("description", out var pd) ? pd.GetString() ?? "" : "";
                                    string paramType = prop.Value.TryGetProperty("type", out var pt) ? pt.GetString() ?? "string" : "string";
                                    propertiesDict[paramName] = new
                                    {
                                        type = paramType,
                                        description = paramDesc
                                    };
                                    if (schemaNode.TryGetProperty("required", out var reqNode) && reqNode.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var r in reqNode.EnumerateArray())
                                        {
                                            if (string.Equals(r.GetString(), paramName, StringComparison.OrdinalIgnoreCase)) requiredList.Add(paramName);
                                        }
                                    }
                                }
                            }

                            var toolNode = new
                            {
                                type = "object",
                                properties = propertiesDict,
                                required = requiredList.ToArray()
                            };

                            var chatTool = new ChatTool
                            {
                                Type = "function",
                                Function = new ChatFunction
                                {
                                    Name = toolName,
                                    Description = description,
                                    Parameters = JsonSerializer.SerializeToNode(toolNode)
                                }
                            };

                            _loadedTools[toolName] = chatTool;
                            _toolEndpoints[toolName] = endpointInfo;
                        }
                    }
                }
                else 
                {
                    throw new Exception("在 OpenAPI JSON 中找不到 'paths' 节点，可能是无效的规范文件。");
                }

                skill.StatusText = "已加载";
                skill.StatusColor = "#FF00FF00";
                skill.IsLoaded = true;
                SaveConfig();
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                skill.StatusText = "加载失败";
                skill.StatusColor = "#FFFF0000";
                skill.IsLoaded = false;
                System.Diagnostics.Debug.WriteLine($"Failed to parse skill url {skill.Url}: {ex.Message}");
                return (false, ex.Message);
            }
        }

        public List<ChatTool> GetTools()
        {
            return _loadedTools.Values.ToList();
        }

        public async Task<string> CallSkillAsync(string toolName, string argsJson)
        {
            if (!_toolEndpoints.TryGetValue(toolName, out var endpoint))
            {
                return $"Error: Skill tool {toolName} not found.";
            }

            try
            {
                var doc = JsonDocument.Parse(argsJson);
                string path = endpoint.Path;

                var queryParams = new List<string>();
                var bodyParams = new Dictionary<string, JsonElement>();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string propName = prop.Name;
                    string propValue = prop.Value.ToString();
                    
                    if (path.Contains($"{{{propName}}}"))
                    {
                        path = path.Replace($"{{{propName}}}", Uri.EscapeDataString(propValue));
                    }
                    else if (endpoint.QueryParamNames.Contains(propName) || (endpoint.Method == "get" && !path.Contains($"{{{propName}}}")))
                    {
                        queryParams.Add($"{Uri.EscapeDataString(propName)}={Uri.EscapeDataString(propValue)}");
                    }
                    else
                    {
                        bodyParams[propName] = prop.Value.Clone();
                    }
                }

                string requestUrl = endpoint.BaseUrl.TrimEnd('/') + path;
                if (queryParams.Any())
                {
                    requestUrl += (requestUrl.Contains('?') ? "&" : "?") + string.Join("&", queryParams);
                }

                string skillId = "";
                var parts = toolName.Split("__");
                if (parts.Length >= 2) skillId = parts[1];
                var skill = Skills.FirstOrDefault(x => x.Id == skillId);
                var client = _httpClientFactory.CreateClient(skill?.UseDomesticNetwork == true ? "DeepSeek" : "ProxyTools");

                if (endpoint.Method == "get")
                {
                    var resp = await client.GetAsync(requestUrl);
                    return await resp.Content.ReadAsStringAsync();
                }
                else if (endpoint.Method == "post" || endpoint.Method == "put" || endpoint.Method == "patch")
                {
                    string bodyJson = bodyParams.Count > 0 ? JsonSerializer.Serialize(bodyParams) : argsJson;
                    var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                    HttpResponseMessage resp;
                    if (endpoint.Method == "put") resp = await client.PutAsync(requestUrl, content);
                    else if (endpoint.Method == "patch") resp = await client.PatchAsync(requestUrl, content);
                    else resp = await client.PostAsync(requestUrl, content);
                    return await resp.Content.ReadAsStringAsync();
                }
                else if (endpoint.Method == "delete")
                {
                    var resp = await client.DeleteAsync(requestUrl);
                    return await resp.Content.ReadAsStringAsync();
                }

                return "Unsupported HTTP Method.";
            }
            catch (Exception ex)
            {
                return $"Error calling remote skill API: {ex.Message}";
            }
        }

        private class SkillEndpointInfo
        {
            public string BaseUrl { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string Method { get; set; } = string.Empty;
            public HashSet<string> QueryParamNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
