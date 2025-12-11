using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }
    }
}