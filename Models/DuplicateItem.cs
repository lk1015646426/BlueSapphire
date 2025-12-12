using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace BlueSapphire.Models
{
    // ==========================================
    // 2. 数据模型: DuplicateItem (去重弹窗专用)
    // ==========================================
    public partial class DuplicateItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public StorageFile? File { get; }
        public bool IsGroupSeparator { get; }

        // 使用 ?. 操作符安全访问
        public string DisplayName => File?.Name ?? "重复组";
        public bool IsKeepSuggestion { get; }

        // --- UI 绑定属性 ---
        public Visibility SeparatorVisibility => IsGroupSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CheckBoxVisibility => !IsGroupSeparator ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SuggestionVisibility => IsKeepSuggestion ? Visibility.Visible : Visibility.Collapsed;

        // 安全访问 DateCreated
        public string DateString => File?.DateCreated.ToString("yyyy-MM-dd HH:mm") ?? "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); } }
        }

        // 构造函数：用于文件项
        public DuplicateItem(StorageFile file, bool isKeepSuggestion)
        {
            File = file;
            IsKeepSuggestion = isKeepSuggestion;
            IsGroupSeparator = false;
            IsChecked = !isKeepSuggestion;
        }

        // 构造函数：用于分隔符
        private DuplicateItem(bool isGroupSeparator)
        {
            File = null;
            IsKeepSuggestion = false;
            IsGroupSeparator = isGroupSeparator;
            IsChecked = false;
        }

        public static DuplicateItem CreateSeparator() => new DuplicateItem(true);
    }
}