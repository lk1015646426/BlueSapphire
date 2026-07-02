using System;
using System.Text.Json.Serialization;

namespace BlueSapphire.Models
{
    public class AgentSkillConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Unknown Skill";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("use_domestic_network")]
        public bool UseDomesticNetwork { get; set; } = false;

        [JsonPropertyName("instructions")]
        public string Instructions { get; set; } = "";

        [JsonPropertyName("added_at")]
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
