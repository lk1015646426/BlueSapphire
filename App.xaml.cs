using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
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
using BlueSapphire.Helpers;

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
            RequestedTheme = ResolveRequestedApplicationTheme(
                AppSettings.Get("AppTheme", "System") ?? "System");
            this.InitializeComponent();
        }

        private static ApplicationTheme ResolveRequestedApplicationTheme(string preference)
        {
            return preference switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => ResolveSystemTheme() == ElementTheme.Light
                    ? ApplicationTheme.Light
                    : ApplicationTheme.Dark
            };
        }

        public static void ApplyThemePreference(string preference)
        {
            ElementTheme theme = preference switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ResolveSystemTheme()
            };
            bool highContrast = false;
            try
            {
                highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
            }
            catch
            {
                // 某些精简版系统不提供辅助功能状态，继续使用所选主题。
            }

            if (CurrentWindow?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
            else if (Application.Current is App app)
            {
                app.RequestedTheme = theme == ElementTheme.Light
                    ? ApplicationTheme.Light
                    : ApplicationTheme.Dark;
            }

            ApplyColorPalette(highContrast ? "HighContrast" : theme == ElementTheme.Light ? "Light" : "Dark");
        }

        private static void ApplyColorPalette(string palette)
        {
            IReadOnlyDictionary<string, string> colors = palette switch
            {
                "Light" => new Dictionary<string, string>
                {
                    ["BgColor"] = "#F5FAFA", ["PanelSurface"] = "#FFFFFF",
                    ["PanelSurfaceStrong"] = "#EDF6F6", ["SurfaceElevated"] = "#E2F0F1",
                    ["PanelHighlight"] = "#D6EAEC", ["BorderColor"] = "#2A0B2630",
                    ["BorderSubtle"] = "#160B2630", ["BorderActive"] = "#500B2630",
                    ["TextMain"] = "#102529", ["TextSecondary"] = "#36545A",
                    ["TextMuted"] = "#526D72", ["TextFaint"] = "#61777B",
                    ["AccentCyan"] = "#087C93", ["AccentCyanBg"] = "#1A087C93",
                    ["AccentSafe"] = "#087A55", ["AccentSafeBg"] = "#18087A55",
                    ["AccentReview"] = "#805300", ["AccentReviewBg"] = "#1A805300",
                    ["AccentInspect"] = "#096D9A", ["AccentInspectBg"] = "#18096D9A",
                    ["AccentDanger"] = "#B4233C", ["AccentDangerBg"] = "#18B4233C",
                    ["OverlayScrim"] = "#E6FFFFFF", ["MediaSurface"] = "#E7EFF1",
                    ["BadgeSurface"] = "#E6FFFFFF"
                },
                "HighContrast" => new Dictionary<string, string>
                {
                    ["BgColor"] = "#FF000000", ["PanelSurface"] = "#FF000000",
                    ["PanelSurfaceStrong"] = "#FF000000", ["SurfaceElevated"] = "#FF000000",
                    ["PanelHighlight"] = "#FF202020", ["BorderColor"] = "#FFFFFFFF",
                    ["BorderSubtle"] = "#FFFFFFFF", ["BorderActive"] = "#FFFFFF00",
                    ["TextMain"] = "#FFFFFFFF", ["TextSecondary"] = "#FFFFFFFF",
                    ["TextMuted"] = "#FFE0E0E0", ["TextFaint"] = "#FFC0C0C0",
                    ["AccentCyan"] = "#FFFFFF00", ["AccentCyanBg"] = "#FF000000",
                    ["AccentSafe"] = "#FF00FF00", ["AccentSafeBg"] = "#FF000000",
                    ["AccentReview"] = "#FFFFFF00", ["AccentReviewBg"] = "#FF000000",
                    ["AccentInspect"] = "#FF00FFFF", ["AccentInspectBg"] = "#FF000000",
                    ["AccentDanger"] = "#FFFF0000", ["AccentDangerBg"] = "#FF000000",
                    ["OverlayScrim"] = "#FF000000", ["MediaSurface"] = "#FF000000",
                    ["BadgeSurface"] = "#FF000000"
                },
                _ => new Dictionary<string, string>
                {
                    ["BgColor"] = "#080D0F", ["PanelSurface"] = "#0C1518",
                    ["PanelSurfaceStrong"] = "#0F1C20", ["SurfaceElevated"] = "#13252B",
                    ["PanelHighlight"] = "#163039", ["BorderColor"] = "#1AFFFFFF",
                    ["BorderSubtle"] = "#0DFFFFFF", ["BorderActive"] = "#2AFFFFFF",
                    ["TextMain"] = "#E8F4F1", ["TextSecondary"] = "#A8C0BD",
                    ["TextMuted"] = "#8FA8A6", ["TextFaint"] = "#6B8786",
                    ["AccentCyan"] = "#22D3EE", ["AccentCyanBg"] = "#1422D3EE",
                    ["AccentSafe"] = "#34D399", ["AccentSafeBg"] = "#1434D399",
                    ["AccentReview"] = "#FBBF24", ["AccentReviewBg"] = "#14FBBF24",
                    ["AccentInspect"] = "#38BDF8", ["AccentInspectBg"] = "#1438BDF8",
                    ["AccentDanger"] = "#FB7185", ["AccentDangerBg"] = "#22FB7185",
                    ["OverlayScrim"] = "#E6080D0F", ["MediaSurface"] = "#0C1116",
                    ["BadgeSurface"] = "#E6101418"
                }
            };

            if (Application.Current is not App app) return;
            foreach ((string key, string value) in colors)
            {
                ResourceDictionary? owner = FindResourceOwner(app.Resources, key);
                if (owner?[key] is SolidColorBrush brush)
                {
                    brush.Color = ParseColor(value);
                }
            }

            if (CurrentWindow is MainWindow mainWindow)
            {
                mainWindow.ApplyThemeChrome(
                    palette == "Light" ? ElementTheme.Light : ElementTheme.Dark);
            }
        }

        private static ResourceDictionary? FindResourceOwner(ResourceDictionary dictionary, string key)
        {
            if (dictionary.ContainsKey(key)) return dictionary;
            foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
            {
                ResourceDictionary? owner = FindResourceOwner(merged, key);
                if (owner != null) return owner;
            }
            return null;
        }

        private static Windows.UI.Color ParseColor(string hex)
        {
            string value = hex.TrimStart('#');
            uint packed = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte alpha = value.Length == 8 ? (byte)(packed >> 24) : (byte)255;
            byte red = (byte)(packed >> 16);
            byte green = (byte)(packed >> 8);
            byte blue = (byte)packed;
            return Windows.UI.Color.FromArgb(alpha, red, green, blue);
        }

        private static ElementTheme ResolveSystemTheme()
        {
            try
            {
                Windows.UI.Color background = new Windows.UI.ViewManagement.UISettings()
                    .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
                double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255d;
                return luminance > 0.5 ? ElementTheme.Light : ElementTheme.Dark;
            }
            catch
            {
                return ElementTheme.Dark;
            }
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
            services.AddTransient<BlueSapphire.Tools.DevLogTool>();
            services.AddTransient<BlueSapphire.Tools.AboutTool>();
            // ✅ 新增：注册我们的重命名业务服务 (使用 Singleton 单例即可，因为它是无状态的工具类)
            services.AddSingleton<BlueSapphire.Services.MediaRenameService>();

            // ✅ 新增：注册去重扫描业务服务
            services.AddSingleton<BlueSapphire.Services.MediaDeduplicationService>();

            // 注册 AI 服务
            services.AddSingleton<BlueSapphire.Services.DeepSeekAIService>();
            services.AddSingleton<BlueSapphire.Services.AIToolsRegistry>();
            services.AddSingleton<BlueSapphire.Services.AIClassifierService>();
            services.AddSingleton<BlueSapphire.Services.AIChatHistoryService>();

            // 注册我们的本地文件系统服务 (回收站功能)
            services.AddSingleton<BlueSapphire.Services.NativeFileService>();
            services.AddSingleton<BlueSapphire.Services.ImageProcessingService>();
            services.AddSingleton<BlueSapphire.Services.ImageMetadataService>();
            services.AddSingleton<BlueSapphire.Services.MediaTagService>();
            services.AddSingleton<BlueSapphire.Services.DevLogDataService>();
            services.AddSingleton<BlueSapphire.Services.McpServerManager>();
            services.AddSingleton<BlueSapphire.Services.WebSkillManager>();
            services.AddSingleton<BlueSapphire.Services.AgentSkillManager>();
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
            services.AddHttpClient("ExternalSafe")
                .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
                {
                    AllowAutoRedirect = false
                });
            services.AddHttpClient("DeepSeek")
                .ConfigureHttpClient(client =>
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "BlueSapphire/1.0");
                    client.Timeout = TimeSpan.FromSeconds(120);
                })
                .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false
                });

            services.AddHttpClient("ProxyTools").ConfigurePrimaryHttpMessageHandler(() => {
                var handler = new System.Net.Http.HttpClientHandler();
                handler.AllowAutoRedirect = false;
                int[] commonPorts = { 7897, 7890, 10809, 10808, 10810, 10811 };
                try
                {
                    var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                    var listeners = properties.GetActiveTcpListeners();
                    foreach (var port in commonPorts)
                    {
                        if (System.Linq.Enumerable.Any(listeners, l => (l.Address.ToString() == "127.0.0.1" || l.Address.ToString() == "0.0.0.0") && l.Port == port))
                        {
                            handler.Proxy = new System.Net.WebProxy($"http://127.0.0.1:{port}");
                            handler.UseProxy = true;
                            break;
                        }
                    }
                }
                catch { }
                return handler;
            });
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
            ApplyThemePreference(AppSettings.Get("AppTheme", "System") ?? "System");
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
                (Current as App)?.Services?.GetService<ILogger<App>>()?.LogCritical(exception, "[{Source}] 未处理异常: {Message}", source, message);
            }
            catch
            {
                // 异常日志不能再向外抛，否则会遮蔽原始故障。
            }
        }
    }
}

