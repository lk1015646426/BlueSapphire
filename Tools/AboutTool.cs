using System;
using BlueSapphire.Interfaces;
using Microsoft.UI.Xaml.Controls;

namespace BlueSapphire.Tools
{
    public sealed class AboutTool : ITool
    {
        public string Id => "About";
        public string Title => "关于";
        public Symbol Icon => Symbol.Help;
        public Type ContentPage => typeof(AboutPage);
        public void Initialize() { }
    }
}
