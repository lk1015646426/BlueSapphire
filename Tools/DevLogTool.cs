using System;
using Microsoft.UI.Xaml.Controls;
using BlueSapphire.Interfaces;
using BlueSapphire.Views;

namespace BlueSapphire.Tools
{
    public class DevLogTool : ITool
    {
        // 工具的唯一标识符
        public string Id => "EvolutionLog";

        // 在侧边导航栏中显示的名称
        public string Title => "跃迁记录";

        // 【修复点】：改为 Symbol.Library (资料库) 或 Symbol.Document (文档)
        public Symbol Icon => Symbol.Library;

        // 对应的主内容页面类型
        public Type ContentPage => typeof(DevLogPage);

        public void Initialize()
        {
        }
    }
}