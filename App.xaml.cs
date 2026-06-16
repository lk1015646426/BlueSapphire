using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
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
using Microsoft.Extensions.Logging;
using BlueSapphire.Tools;
using BlueSapphire.ViewModels;
using BlueSapphire.Services.Logging;

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
        public static string LaunchArguments { get; private set; } = string.Empty;

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
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            this.InitializeComponent();
        }

        // ✅ 新增 4：注册所有的 Tool 和 ViewModel
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // 注册工具
            services.AddTransient<HomeTool>();
            services.AddTransient<MediaManagerTool>();
            services.AddTransient<CleanerAssistantTool>();
            services.AddTransient<BlueSapphire.Tools.AICopilotTool>();
            // ✅ 新增：注册我们的重命名业务服务 (使用 Singleton 单例即可，因为它是无状态的工具类)
            services.AddSingleton<BlueSapphire.Services.MediaRenameService>();

            // ✅ 新增：注册去重扫描业务服务
            services.AddSingleton<BlueSapphire.Services.MediaDeduplicationService>();

            // 注册 AI 服务
            services.AddSingleton<BlueSapphire.Services.DeepSeekAIService>();
            services.AddSingleton<BlueSapphire.Services.AIToolsRegistry>();
            services.AddSingleton<BlueSapphire.Services.AIClassifierService>();

            // 注册我们的本地文件系统服务 (回收站功能)
            services.AddSingleton<BlueSapphire.Services.NativeFileService>();
            services.AddSingleton<BlueSapphire.Services.ImageProcessingService>();
            services.AddSingleton<BlueSapphire.Services.ImageMetadataService>();
            services.AddSingleton<BlueSapphire.Services.MediaTagService>();
            services.AddSingleton<BlueSapphire.Services.DevLogDataService>();
            services.AddSingleton<BlueSapphire.Services.CleanerRuleService>();
            services.AddSingleton<BlueSapphire.Services.CleanerStateStore>();
            services.AddSingleton<BlueSapphire.Services.CleanerRiskEvaluator>();
            services.AddSingleton<BlueSapphire.Services.CleanerLockService>();
            services.AddSingleton<BlueSapphire.Services.CleanerOrphanResidueService>();
            services.AddSingleton<BlueSapphire.Services.CleanerPrivilegeService>();
            services.AddSingleton<BlueSapphire.Services.CleanerLaunchActionService>();
            services.AddSingleton<BlueSapphire.Services.CleanerDriveService>();
            services.AddSingleton<BlueSapphire.Services.CleanerBoundaryGuard>();
            services.AddSingleton<BlueSapphire.Services.CleanerAuditService>();
            services.AddSingleton<BlueSapphire.Services.CleanerProfileService>();
            services.AddSingleton<BlueSapphire.Services.CleanerAutomationScheduleService>();
            services.AddSingleton<BlueSapphire.Services.CleanerAutomationService>();
            services.AddSingleton<BlueSapphire.Services.CleanerTelemetryService>();
            services.AddSingleton<BlueSapphire.Services.CleanerRecommendationService>();
            services.AddSingleton<BlueSapphire.Services.CleanerSpaceAnalysisService>();
            services.AddSingleton<BlueSapphire.Services.CleanerScanService>();
            services.AddSingleton<BlueSapphire.Services.CleanerDeepScanService>();
            services.AddSingleton<BlueSapphire.Services.CleanerExecutionService>();
            services.AddSingleton<BlueSapphire.Services.AIMemoryService>();

            // 注册 ViewModel
            services.AddTransient<MediaManagerViewModel>();
            services.AddTransient<DevLogViewModel>();
            services.AddTransient<CleanerAssistantViewModel>();
            services.AddTransient<CleanerSettingsViewModel>();

            services.AddHttpClient();
            services.AddLogging(builder => builder.AddFileLogger());
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            LaunchArguments = args.Arguments ?? string.Empty;
            
            var logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Blue Sapphire 引擎点火成功。");

            // 实例化 MainWindow 并赋值给静态属性
            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogUnhandledException("XamlUnhandledException", e.Exception, e.Message);
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            LogUnhandledException(
                "AppDomainUnhandledException",
                e.ExceptionObject as Exception,
                e.ExceptionObject?.ToString() ?? "未知异常");
        }

        private static void LogUnhandledException(string source, Exception? exception, string message)
        {
            try
            {
                string rootPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlueSapphire",
                    "Diagnostics");
                Directory.CreateDirectory(rootPath);

                string logPath = System.IO.Path.Combine(rootPath, "unhandled-exceptions.log");
                StringBuilder builder = new();
                builder.AppendLine("============================================================");
                builder.AppendLine($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
                builder.AppendLine($"Source: {source}");
                builder.AppendLine($"LaunchArguments: {LaunchArguments}");
                builder.AppendLine($"Message: {message}");
                if (exception != null)
                {
                    builder.AppendLine($"ExceptionType: {exception.GetType().FullName}");
                    builder.AppendLine("Exception:");
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 异常日志不能再向外抛，否则会遮蔽原始故障。
            }
        }
    }
}

