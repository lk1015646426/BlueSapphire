using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;
using BlueSapphire.Models; // 引入 Models 命名空间以使用 DevLogStatus

namespace BlueSapphire
{
    // ==========================================
    // 转换器类 (IValueConverter)
    // ==========================================

    /// <summary>
    /// 1. 基础布尔转可见性转换器 (True -> Visible, False -> Collapsed)
    /// </summary>
    public class BoolToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    /// <summary>
    /// 2. 反向布尔转可见性转换器 (True -> Collapsed, False -> Visible)
    /// </summary>
    public class InverseBoolToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    /// <summary>
    /// 3. 布尔转 [建议保留] 文本转换器
    /// </summary>
    public class BoolToKeepTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? " [建议保留]" : string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    /// <summary>
    /// 4. 日期格式化转换器 (用于显示文件创建日期)
    /// </summary>
    public class DateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTimeOffset dto)
            {
                return dto.ToString("yyyy-MM-dd HH:mm");
            }
            return string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    /// <summary>
    /// 5. 状态转颜色转换器 (用于 DevLog 节点颜色映射)
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DevLogStatus status)
            {
                return status switch
                {
                    DevLogStatus.Pending => ThemeBrush("TextMuted"),
                    DevLogStatus.InProgress => ThemeBrush("AccentReview"),
                    DevLogStatus.Completed => ThemeBrush("AccentSafe"),
                    _ => ThemeBrush("TextMuted")
                };
            }
            return ThemeBrush("TextMuted");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }

        private static Brush ThemeBrush(string key) =>
            Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
                ? brush
                : new SolidColorBrush(Colors.Transparent);
    }

    /// <summary>
    /// 6. 磁盘使用率转进度条颜色转换器
    ///    < 70%  → 绿 (AccentSafe)
    ///    70-85% → 橙 (AccentReview)
    ///    > 85%  → 红 (AccentDanger)
    /// </summary>
    public class UsageToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double pct)
            {
                if (pct > 85)
                    return ThemeBrush("AccentDanger");
                if (pct > 70)
                    return ThemeBrush("AccentReview");
                return ThemeBrush("AccentSafe");
            }
            return ThemeBrush("AccentSafe");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }

        private static Brush ThemeBrush(string key) =>
            Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
                ? brush
                : new SolidColorBrush(Colors.Transparent);
    }
}
