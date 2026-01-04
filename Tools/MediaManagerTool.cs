using System;
using Microsoft.UI.Xaml.Controls; // 用于 Symbol
using BlueSapphire.Interfaces;      // 用于 ITool
using BlueSapphire.Tools;           // 命名空间建议

namespace BlueSapphire.Tools
{
    public class MediaManagerTool : ITool
    {
        public string Id => "MediaManager";
        public string Title => "媒体管家";
        public Symbol Icon => Symbol.Pictures;

        // 关键点：这里只返回类型 (typeof)，不实例化 Page
        public Type ContentPage => typeof(MediaManagerPage);

        public void Initialize()
        {
            // 如果有初始化逻辑写在这里
        }
    }
}