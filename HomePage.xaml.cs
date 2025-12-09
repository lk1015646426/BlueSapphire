using Microsoft.UI.Xaml.Controls;
using System;
using BlueSapphire.Interfaces;

namespace BlueSapphire
{
    public sealed partial class HomePage : Page, ITool
    {
        // ITool 接口实现
        public string Id => "Home";
        public string Title => "仪表盘";
        public Symbol Icon => Symbol.Home;
        public Type ContentPage => typeof(HomePage);

        public HomePage()
        {
            this.InitializeComponent();
            // v0.5 极简重构：移除了所有仪表盘逻辑，只保留纯净背景
        }

        public void Initialize()
        {
            // 接口要求，留空
        }
    }
}