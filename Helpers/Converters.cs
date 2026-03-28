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
                    DevLogStatus.Pending => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 100, 100)), // 暗灰
                    DevLogStatus.InProgress => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0)), // 呼吸橙
                    DevLogStatus.Completed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 255, 204)), // 霓虹青
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
