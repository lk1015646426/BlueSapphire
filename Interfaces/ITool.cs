using System;
using Microsoft.UI.Xaml.Controls;

namespace BlueSapphire.Interfaces
{
    /// <summary>
    /// 蓝宝石工具箱插件接口 (v0.3 Alpha)
    /// 所有外部工具模块必须实现此接口才能被宿主程序加载。
    /// </summary>
    public interface ITool
    {
        /// <summary>
        /// 工具的唯一标识符（例如："ImageManager"）
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 工具在导航栏中显示的名称（例如："图片管家"）
        /// </summary>
        string Title { get; }

        /// <summary>
        /// 工具在导航栏中显示的图标（例如：Symbol.Pictures）
        /// </summary>
        Symbol Icon { get; }

        /// <summary>
        /// 工具对应的主内容页面类型。
        /// 宿主程序将使用此 Type 在 ContentFrame 中创建和导航页面。
        /// </summary>
        Type ContentPage { get; }

        /// <summary>
        /// 工具模块初始化方法。
        /// </summary>
        void Initialize();
    }
}