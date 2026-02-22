using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using BlueSapphire.Interfaces;

namespace BlueSapphire
{
    public sealed partial class HomePage : Page, ITool
    {
        public string Id => "Home";
        public string Title => "控制中枢";
        public Symbol Icon => Symbol.Home;
        public System.Type ContentPage => typeof(HomePage);

        // 24小时趣味陪伴文案库
        private readonly string[] HourlyGreetings = new string[]
        {
            "世界已静音，控制中枢依然在线。",
            "灵感还在加班，你要不要稍微歇会儿？",
            "现在的每一行代码，都是写给未来的诗。",
            "只有星光和屏幕亮着，挺酷的，但该睡了。",
            "你是在准备迎接日出，还是刚要入梦？",
            "天边快有光了，今天也要元气满满。",
            "早起的鸟儿有虫吃，早起的人有蓝宝石。",
            "给自己泡杯热饮，系统引擎已为您就绪。",
            "整理一下思绪，我们准备好出发了。",
            "开始专注吧，我会安静地守在后台。",
            "各项模块运转平稳，你也该喝口水了。",
            "离午餐还有一小时，最后冲刺一下？",
            "干饭时间到！让大脑和处理器都降降温。",
            "午后的阳光不错，适合闭眼小憩片刻。",
            "各项系统模块正全速为您护航。",
            "喝杯茶放松一下吧，系统由我看着。",
            "夕阳快来了，现在的效率通常是最高的。",
            "接近下班时间，心情是不是也轻快了起来？",
            "晚霞很美，记得抬头看看窗外。",
            "忙碌了一天，蓝宝石正在进行自我维护。",
            "夜幕降临，已为您开启静谧沉浸模式。",
            "晚安。整理一下今天的收获，明天会更好。",
            "月色不错，享受这段独属于你的安静时光。",
            "又过了一天，你离目标又近了一步。"
        };

        public HomePage()
        {
            this.InitializeComponent();
        }

        public void Initialize() { }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateHourlyGreeting();
            CoreIdleAnimation.Begin();
            EntranceStoryboard.Begin();
        }

        private void UpdateHourlyGreeting()
        {
            int hour = DateTime.Now.Hour;
            if (hour >= 0 && hour < HourlyGreetings.Length)
            {
                GreetingText.Text = HourlyGreetings[hour];
            }
        }
    }
}