using BlueSapphire.Interfaces;
using BlueSapphire.Views;
using Microsoft.UI.Xaml.Controls;
using System;

namespace BlueSapphire.Tools
{
    public sealed class AITaskCenterTool : ITool
    {
        public string Id => "AITaskCenter";
        public string Title => "任务中心";
        public Symbol Icon => Symbol.Clock;
        public Type ContentPage => typeof(AITaskCenterPage);

        public void Initialize()
        {
        }
    }
}
