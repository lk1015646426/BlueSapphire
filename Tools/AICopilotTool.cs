using BlueSapphire.Interfaces;
using BlueSapphire.Views;
using Microsoft.UI.Xaml.Controls;
using System;

namespace BlueSapphire.Tools
{
    public class AICopilotTool : ITool
    {
        public string Id => "AICopilotTool";
        public string Title => "AI 智能助手";
        public Symbol Icon => Symbol.Message;
        public Type ContentPage => typeof(AICopilotPage);

        public void Initialize()
        {
        }

        public void Shutdown()
        {
        }
    }
}
