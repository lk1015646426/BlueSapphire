using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;

namespace BlueSapphire.Services
{
    public class DeepSeekAIService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public DeepSeekAIService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ChatMessage> SendChatAsync(List<ChatMessage> messages, List<ChatTool>? tools = null, CancellationToken cancellationToken = default)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
            string? apiKey = AppSettings.GetSecret($"DeepSeekApiKey_{provider}");
            if (string.IsNullOrWhiteSpace(apiKey)) apiKey = AppSettings.GetSecret("DeepSeekApiKey");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ChatMessage { Role = "assistant", Content = "错误：请先在设置中配置对应的 API Key。" };
            }

            string apiUrl = provider == "SiliconFlow" 
                ? "https://api.siliconflow.cn/v1/chat/completions" 
                : "https://api.deepseek.com/v1/chat/completions";
            
            string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
            string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel));
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = defaultModel;
            }

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                tools = tools,
                temperature = 0.6
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            var jsonContent = new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json");

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BlueSapphire/1.0");

            try
            {
                var response = await httpClient.PostAsync(apiUrl, jsonContent, cancellationToken);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new ChatMessage { Role = "assistant", Content = $"API 请求失败: {response.StatusCode}\n{responseString}" };
                }

                var jsonDoc = JsonDocument.Parse(responseString);
                var choice = jsonDoc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");

                return JsonSerializer.Deserialize<ChatMessage>(message.GetRawText()) ?? new ChatMessage { Role = "assistant", Content = "API 解析失败" };
            }
            catch (Exception ex)
            {
                return new ChatMessage { Role = "assistant", Content = $"发生错误: {ex.Message}" };
            }
        }

        public async Task<(List<string> Models, string? Error)> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
            string? apiKey = AppSettings.GetSecret($"DeepSeekApiKey_{provider}");
            if (string.IsNullOrWhiteSpace(apiKey)) apiKey = AppSettings.GetSecret("DeepSeekApiKey");

            if (string.IsNullOrWhiteSpace(apiKey)) return (new List<string>(), "API Key 未配置");

            string apiUrl = provider == "SiliconFlow" 
                ? "https://api.siliconflow.cn/v1/models" 
                : "https://api.deepseek.com/v1/models";

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BlueSapphire/1.0");

            try
            {
                var response = await httpClient.GetAsync(apiUrl, cancellationToken);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (new List<string>(), $"请求失败 ({response.StatusCode}): {responseString}");
                }

                var jsonDoc = JsonDocument.Parse(responseString);
                
                var models = new List<string>();
                if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idElement))
                        {
                            models.Add(idElement.GetString() ?? "");
                        }
                    }
                }
                return (models, null);
            }
            catch (Exception ex)
            {
                return (new List<string>(), $"网络异常: {ex.Message}");
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var result = await GetAvailableModelsAsync();
                return result.Models.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> SendChatStreamAsync(List<ChatMessage> messages, List<ChatTool>? tools = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
            string? apiKey = AppSettings.GetSecret($"DeepSeekApiKey_{provider}");
            if (string.IsNullOrWhiteSpace(apiKey)) apiKey = AppSettings.GetSecret("DeepSeekApiKey");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                yield return new ChatStreamEvent { ContentDelta = "错误：请先在设置中配置对应的 API Key。" };
                yield break;
            }

            string apiUrl = provider == "SiliconFlow" 
                ? "https://api.siliconflow.cn/v1/chat/completions" 
                : "https://api.deepseek.com/v1/chat/completions";
            
            string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
            string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel));
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = defaultModel;
            }

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                tools = tools,
                temperature = 0.6,
                stream = true
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BlueSapphire/1.0");

            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = jsonContent };
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                yield return new ChatStreamEvent { ContentDelta = $"API 请求失败: {response.StatusCode}\n{err}" };
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    ChatStreamEvent streamEvent = new ChatStreamEvent();
                    bool hasData = false;

                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(data);
                        var choices = jsonDoc.RootElement.GetProperty("choices");
                        if (choices.GetArrayLength() > 0)
                        {
                            var delta = choices[0].GetProperty("delta");
                            
                            if (delta.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                            {
                                streamEvent.ContentDelta = contentProp.GetString();
                                hasData = true;
                            }
                            if (delta.TryGetProperty("role", out var roleProp) && roleProp.ValueKind == JsonValueKind.String)
                            {
                                streamEvent.Role = roleProp.GetString();
                                hasData = true;
                            }
                            if (delta.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
                            {
                                streamEvent.ToolCallFragments = new List<ToolCallFragment>();
                                foreach (var tc in toolCallsProp.EnumerateArray())
                                {
                                    var frag = new ToolCallFragment();
                                    if (tc.TryGetProperty("index", out var indexProp)) frag.Index = indexProp.GetInt32();
                                    if (tc.TryGetProperty("id", out var idProp)) frag.Id = idProp.GetString();
                                    if (tc.TryGetProperty("type", out var typeProp)) frag.Type = typeProp.GetString();
                                    if (tc.TryGetProperty("function", out var funcProp))
                                    {
                                        if (funcProp.TryGetProperty("name", out var nameProp)) frag.FunctionName = nameProp.GetString();
                                        if (funcProp.TryGetProperty("arguments", out var argProp)) frag.FunctionArgumentsDelta = argProp.GetString();
                                    }
                                    streamEvent.ToolCallFragments.Add(frag);
                                }
                                hasData = true;
                            }
                        }
                    }
                    catch { }

                    if (hasData)
                    {
                        yield return streamEvent;
                    }
                }
            }
        }
    }

    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; set; }
        
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? ToolCalls { get; set; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }
    }

    public class ChatTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public ChatFunction Function { get; set; } = new();
    }

    public class ChatFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    public class ChatStreamEvent
    {
        public string? ContentDelta { get; set; }
        public string? Role { get; set; }
        public List<ToolCallFragment>? ToolCallFragments { get; set; }
    }

    public class ToolCallFragment
    {
        public int Index { get; set; }
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? FunctionName { get; set; }
        public string? FunctionArgumentsDelta { get; set; }
    }
}
