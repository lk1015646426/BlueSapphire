using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// ✅ 新增的命名空间，用于依赖注入
using Microsoft.Extensions.DependencyInjection;
using BlueSapphire.Tools;
using BlueSapphire.ViewModels;

namespace BlueSapphire
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        // [优化修改]
        // 将原有的 private Window? _window; 替换为全局静态属性。
        // 这样子页面(如 MediaManagerPage)可以通过 App.CurrentWindow 获取句柄。
        public static Window? CurrentWindow { get; private set; }

        // ✅ 新增 1：方便全局获取 App 实例
        public new static App Current => (App)Application.Current;

        // ✅ 新增 2：定义全局服务提供者 (DI 容器的核心)
        public IServiceProvider Services { get; }

        // [新增修复] 
        // 提供一个静态属性来获取主窗口句柄 (IntPtr)，
        // 专门供 PickFolderAsync 等需要窗口句柄的方法使用。
        public static IntPtr MainWindowHandle
        {
            get
            {
                if (CurrentWindow == null) return IntPtr.Zero;
                // 使用 WinRT 互操作库从 Window 对象获取原生句柄
                return WinRT.Interop.WindowNative.GetWindowHandle(CurrentWindow);
            }
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // ✅ 新增 3：在程序最开始初始化 DI 容器
            Services = ConfigureServices();
            this.InitializeComponent();
        }

        // ✅ 新增 4：注册所有的 Tool 和 ViewModel
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // 注册工具
            services.AddTransient<HomeTool>();
            services.AddTransient<MediaManagerTool>();

            // ✅ 新增：注册我们的重命名业务服务 (使用 Singleton 单例即可，因为它是无状态的工具类)
            services.AddSingleton<BlueSapphire.Services.MediaRenameService>();

            // ✅ 新增：注册去重扫描业务服务
            services.AddSingleton<BlueSapphire.Services.MediaDeduplicationService>();

            // 注册 ViewModel
            services.AddTransient<MediaManagerViewModel>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // [优化修改]
            // 实例化 MainWindow 并赋值给静态属性
            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();
        }
    }
}