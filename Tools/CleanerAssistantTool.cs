using BlueSapphire.Interfaces;
using Microsoft.UI.Xaml.Controls;
using System;

namespace BlueSapphire.Tools
{
    public class CleanerAssistantTool : ITool
    {
        public string Id => "CleanerAssistant";
        public string Title => "清理助手";
        public Symbol Icon => Symbol.Delete;
        public Type ContentPage => typeof(CleanerAssistantPage);

        public void Initialize()
        {
        }
    }
}
