using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace BlueSapphire
{
    public sealed partial class AboutPage : Page
    {
        // 新增：用于记录点击次数
        private int _versionTapCount = 0;

        public AboutPage()
        {
            this.InitializeComponent();
        }

        // 修改这个现有的点击事件
        private void TitleContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _versionTapCount++;

            // 连按 5 次触发彩蛋
            if (_versionTapCount >= 5)
            {
                _versionTapCount = 0; // 重置计数器

                // 核心：让当前 Frame 导航到隐藏的 DevLogPage
                this.Frame.Navigate(typeof(Views.DevLogPage));
            }
        }
    }
}