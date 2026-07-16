using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace BlueSapphire.Models
{
    public class WebSkillConfig : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public string Id { get; set; } = Guid.NewGuid().ToString();

        private string _name = "未命名技能";
        public string Name 
        { 
            get => _name; 
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Url { get; set; } = string.Empty;

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(TrustStatusText));
            }
        }

        private bool _isTrusted;
        public bool IsTrusted
        {
            get => _isTrusted;
            set
            {
                if (_isTrusted == value) return;
                _isTrusted = value;
                OnPropertyChanged(nameof(IsTrusted));
                OnPropertyChanged(nameof(TrustStatusText));
            }
        }

        private bool _useDomesticNetwork = false;
        public bool UseDomesticNetwork 
        { 
            get => _useDomesticNetwork; 
            set 
            { 
                if (_useDomesticNetwork != value)
                {
                    _useDomesticNetwork = value; 
                    OnPropertyChanged(nameof(UseDomesticNetwork));
                    OnPropertyChanged(nameof(NetworkStatusText));
                }
            }
        }

        [JsonIgnore]
        public string NetworkStatusText => UseDomesticNetwork ? "网络：强制国内直连" : "网络：跟随系统代理";

        [JsonIgnore]
        public string TrustStatusText => IsTrusted
            ? (IsEnabled ? "已信任并启用" : "已信任，当前停用")
            : "待审核";

        [JsonIgnore]
        public string ActionText => IsEnabled ? "停用" : "审核并启用";

        private int _toolCount;
        [JsonIgnore]
        public int ToolCount
        {
            get => _toolCount;
            set
            {
                if (_toolCount == value) return;
                _toolCount = value;
                OnPropertyChanged(nameof(ToolCount));
                OnPropertyChanged(nameof(ToolCountText));
            }
        }

        [JsonIgnore]
        public string ToolCountText => ToolCount > 0 ? $"接口工具：{ToolCount} 个" : "接口工具：尚未载入";

        private string _targetOrigin = string.Empty;
        [JsonIgnore]
        public string TargetOrigin
        {
            get => _targetOrigin;
            set
            {
                if (_targetOrigin == value) return;
                _targetOrigin = value;
                OnPropertyChanged(nameof(TargetOrigin));
            }
        }

        private string _statusText = "等待加载";
        [JsonIgnore]
        public string StatusText 
        { 
            get => _statusText; 
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        private string _statusColor = "#A0FFFFFF";
        [JsonIgnore]
        public string StatusColor 
        { 
            get => _statusColor; 
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }
        
        private bool _isLoaded = false;
        [JsonIgnore]
        public bool IsLoaded 
        { 
            get => _isLoaded; 
            set { _isLoaded = value; OnPropertyChanged(nameof(IsLoaded)); }
        }
    }
}
