using System;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Interfaces;
using BlueSapphire.Views;

namespace BlueSapphire.Tools
{
    public class DevLogTool : ITool
    {
        public string Id => "EvolutionLog";

        public string Title => "更新日志";

        // [已修复] 修正为文档相关的图标
        public Symbol Icon => Symbol.Document;

        public Type ContentPage => typeof(DevLogPage);

        public void Initialize()
        {
        }
    }
}
