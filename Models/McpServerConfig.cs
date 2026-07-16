using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlueSapphire.Models
{
    public class McpServerConfig
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = "New MCP Server";
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool IsEnabled { get; set; }
        public bool IsApproved { get; set; }

        [JsonIgnore]
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        public string ProtectedEnvironmentVariables { get; set; } = "";
    }
}
