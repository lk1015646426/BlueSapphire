using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlueSapphire.Helpers;

namespace BlueSapphire.Services
{
    public class DeepSeekAIService
    {
        private readonly HttpClient _httpClient;
        private const string ApiUrl = "https://api.deepseek.com/v1/chat/completions";

        public DeepSeekAIService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<string> SendChatAsync(List<ChatMessage> messages, List<ChatTool>? tools = null)
        {
            string apiKey = AppSettings.Get("DeepSeekApiKey", string.Empty);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "错误：请先在设置中配置 DeepSeek API Key。";
            }

            var requestBody = new
            {
                model = "deepseek-chat",
                messages = messages,
                tools = tools,
                temperature = 0.6
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
                Encoding.UTF8,
                "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await _httpClient.PostAsync(ApiUrl, jsonContent);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return $"API 请求失败: {response.StatusCode}\n{responseString}";
                }

                var jsonDoc = JsonDocument.Parse(responseString);
                var choice = jsonDoc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");

                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    // Handle function call in a basic way: return a special string or serialize
                    return $"[TOOL_CALL] {toolCalls.GetRawText()}";
                }

                return message.GetProperty("content").GetString() ?? "";
            }
            catch (Exception ex)
            {
                return $"发生错误: {ex.Message}";
            }
        }
    }

    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
        
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }
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
}
