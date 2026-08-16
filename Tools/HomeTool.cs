using System;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Interfaces;
using BlueSapphire.Views;

namespace BlueSapphire.Tools
{
    public class HomeTool : ITool
    {
        public string Id => "Home";
        public string Title => "工作台";
        public Symbol Icon => Symbol.Home;

        // 这里的 HomePage 是你的主页类名
        public Type ContentPage => typeof(HomePage);

        public void Initialize() { }
    }
}
