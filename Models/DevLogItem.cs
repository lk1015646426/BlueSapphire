using System;

namespace BlueSapphire.Models
{
    public class DevLogItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = "v0.6.0";
        public bool IsCompleted { get; set; } = false;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}