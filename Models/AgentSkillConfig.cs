using System;
using System.Text.Json.Serialization;
using System.ComponentModel;

namespace BlueSapphire.Models
{
    public class AgentSkillConfig : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "Unknown Skill";
        [JsonPropertyName("name")]
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value ?? string.Empty, nameof(Name));
        }

        private string _description = string.Empty;
        [JsonPropertyName("description")]
        public string Description
        {
            get => _description;
            set => SetField(ref _description, value ?? string.Empty, nameof(Description));
        }

        private string _url = string.Empty;
        [JsonPropertyName("url")]
        public string Url
        {
            get => _url;
            set => SetField(ref _url, value ?? string.Empty, nameof(Url));
        }

        private bool _useDomesticNetwork;
        [JsonPropertyName("use_domestic_network")]
        public bool UseDomesticNetwork
        {
            get => _useDomesticNetwork;
            set => SetField(ref _useDomesticNetwork, value, nameof(UseDomesticNetwork));
        }

        private string _instructions = string.Empty;
        [JsonPropertyName("instructions")]
        public string Instructions
        {
            get => _instructions;
            set => SetField(ref _instructions, value ?? string.Empty, nameof(Instructions));
        }

        private bool _isEnabled;
        [JsonPropertyName("is_enabled")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                NotifyTrustStateChanged();
            }
        }

        private bool _isTrusted;
        [JsonPropertyName("is_trusted")]
        public bool IsTrusted
        {
            get => _isTrusted;
            set
            {
                if (_isTrusted == value) return;
                _isTrusted = value;
                NotifyTrustStateChanged();
            }
        }

        [JsonIgnore]
        public string StatusText => IsTrusted
            ? (IsEnabled ? "已信任并启用" : "已信任，当前停用")
            : "待审核";

        [JsonIgnore]
        public string ActionText => IsEnabled ? "停用" : "审核并启用";

        [JsonPropertyName("added_at")]
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        private void NotifyTrustStateChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTrusted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionText)));
        }

        private void SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
