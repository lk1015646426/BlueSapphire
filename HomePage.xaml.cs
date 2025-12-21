using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using BlueSapphire.Interfaces;

namespace BlueSapphire
{
    public sealed partial class HomePage : Page, ITool
    {
        public string Id => "Home";
        public string Title => "仪表盘";
        public Symbol Icon => Symbol.Home;
        public System.Type ContentPage => typeof(HomePage);

        public HomePage()
        {
            this.InitializeComponent();
        }

        public void Initialize() { }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 启动入场动画
            EntranceStoryboard.Begin();
        }

        // === 交互反馈：鼠标进入卡片时产生高亮光效 ===
        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                // 变亮边框
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 255, 255));
                // 微微放大卡片 (可选)
                card.Opacity = 1.0;
            }
        }

        // === 交互反馈：鼠标离开时恢复暗淡状态 ===
        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                // 恢复半透明边框
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
                card.Opacity = 0.9;
            }
        }
    }
}