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
