using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace BlueSapphire
{
    public sealed partial class AboutPage : Page
    {
        private int _clickCount = 0;
        private DateTime _lastClickTime = DateTime.MinValue;

        public AboutPage()
        {
            this.InitializeComponent();
        }

        private void TitleContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var now = DateTime.Now;
            if ((now - _lastClickTime).TotalMilliseconds > 600) _clickCount = 0;

            _lastClickTime = now;
            _clickCount++;

            if (_clickCount >= 7)
            {
                _clickCount = 0;
                // 注意：这里跳转到 DevLogPage。如果你还没创建该页面，这行会报错。
                // 建议在创建完 DevLogPage 后再取消下面一行的注释。
                // this.Frame.Navigate(typeof(DevLogPage));
            }
        }
    }
}