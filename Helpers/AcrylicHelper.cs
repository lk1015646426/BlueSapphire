using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BlueSapphire.Helpers
{
    /// <summary>
    /// 磨砂玻璃面工厂：以 C# 代码方式创建 AcrylicBrush。
    /// 绕开"AcrylicBrush 作为 XAML 资源元素会让 WindowsAppSDK 1.8 的 XamlCompiler 静默崩溃"的问题——
    /// 凡是需要磨砂玻璃面的地方（弹窗/浮层/Tooltip），都从这里取实例，不要在 XAML 里写 &lt;AcrylicBrush&gt; 资源。
    /// </summary>
    public static class AcrylicHelper
    {
        /// <summary>面板磨砂玻璃：深色面板色 + 轻微模糊，用于弹窗/浮层背景，保证文字可读。</summary>
        public static AcrylicBrush CreatePanelAcrylic()
        {
            // 注意：本版 WinAppSDK 1.8 的 C# 投影不暴露 BackgroundSource / AcrylicBackgroundSource，
            // 默认即 Backdrop（采样正后方内容），正是弹窗所需，无需显式设置。
            return new AcrylicBrush
            {
                TintColor = Color.FromArgb(0xFF, 0x15, 0x1B, 0x1F),
                TintOpacity = 0.72,
                FallbackColor = Color.FromArgb(0xFF, 0x15, 0x1B, 0x1F),
                AlwaysUseFallback = false,
            };
        }
    }
}
