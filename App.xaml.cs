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

            string palette = highContrast ? "HighContrast" : theme == ElementTheme.Light ? "Light" : "Dark";
            ApplyColorPalette(palette, AppSettings.Get("ThemePreset", "default") ?? "default");
        }

        public static void ApplyThemePreset(string preset)
        {
            preset = NormalizeThemePreset(preset);
            AppSettings.Save("ThemePreset", preset);
            ApplyThemePreference(AppSettings.Get("AppTheme", "System") ?? "System");
        }

        public static void ApplyUiFontSize(string size)
        {
            AppSettings.Save("UiFontSize", size);
            double body = size switch { "small" => 13, "medium" => 15, "large" => 16, _ => 14 };
            double caption = size switch { "small" => 11, "medium" => 13, "large" => 14, _ => 12 };
            Application.Current.Resources["ControlContentThemeFontSize"] = body;
            Application.Current.Resources["BodyTextBlockFontSize"] = body;
            Application.Current.Resources["CaptionTextBlockFontSize"] = caption;
        }

        private static void ApplyColorPalette(string palette, string preset)
        {
            Dictionary<string, string> colors = palette switch
            {
                "Light" => new Dictionary<string, string>
                {
                    ["BgColor"] = "#F3EEE6", ["PanelSurface"] = "#FFFCF7",
                    ["PanelSurfaceStrong"] = "#F7F0E8", ["SurfaceElevated"] = "#FFFFFF",
                    ["PanelHighlight"] = "#EEE4D8", ["BorderColor"] = "#2A3B2E29",
                    ["BorderSubtle"] = "#193B2E29", ["BorderActive"] = "#663B2E29",
                    ["TextMain"] = "#2A2520", ["TextSecondary"] = "#554B42",
                    ["TextMuted"] = "#74695E", ["TextFaint"] = "#8C8175",
                    ["TextOnAccent"] = "#FFFFFFFF",
                    ["AccentPrimary"] = "#B34A28", ["AccentPrimaryHover"] = "#A94423",
                    ["AccentPrimaryPressed"] = "#94391F", ["AccentPrimaryBg"] = "#1AB34A28",
                    ["AccentCyan"] = "#B34A28", ["AccentCyanHover"] = "#A94423",
                    ["AccentCyanPressed"] = "#94391F", ["AccentCyanBg"] = "#1AB34A28",
                    ["AccentSafe"] = "#28775B", ["AccentSafeBg"] = "#1828775B",
                    ["AccentReview"] = "#8B611F", ["AccentReviewBg"] = "#188B611F",
                    ["AccentInspect"] = "#66519A", ["AccentInspectBg"] = "#1866519A",
                    ["AccentDanger"] = "#B33E3E", ["AccentDangerBg"] = "#18B33E3E",
                    ["OverlayScrim"] = "#CCF3EEE6", ["MediaSurface"] = "#E8DED2",
                    ["BadgeSurface"] = "#F0FFFCF7"
                },
                "HighContrast" => new Dictionary<string, string>
                {
                    ["BgColor"] = "#FF000000", ["PanelSurface"] = "#FF000000",
                    ["PanelSurfaceStrong"] = "#FF000000", ["SurfaceElevated"] = "#FF000000",
                    ["PanelHighlight"] = "#FF202020", ["BorderColor"] = "#FFFFFFFF",
                    ["BorderSubtle"] = "#FFFFFFFF", ["BorderActive"] = "#FFFFFF00",
                    ["TextMain"] = "#FFFFFFFF", ["TextSecondary"] = "#FFFFFFFF",
                    ["TextMuted"] = "#FFE0E0E0", ["TextFaint"] = "#FFC0C0C0",
                    ["TextOnAccent"] = "#FF000000",
                    ["AccentPrimary"] = "#FFFFFF00", ["AccentPrimaryHover"] = "#FFFFFFFF",
                    ["AccentPrimaryPressed"] = "#FFFFFF00", ["AccentPrimaryBg"] = "#FF000000",
                    ["AccentCyan"] = "#FFFFFF00", ["AccentCyanHover"] = "#FFFFFFFF",
                    ["AccentCyanPressed"] = "#FFFFFF00", ["AccentCyanBg"] = "#FF000000",
                    ["AccentSafe"] = "#FF00FF00", ["AccentSafeBg"] = "#FF000000",
                    ["AccentReview"] = "#FFFFFF00", ["AccentReviewBg"] = "#FF000000",
                    ["AccentInspect"] = "#FF00FFFF", ["AccentInspectBg"] = "#FF000000",
                    ["AccentDanger"] = "#FFFF0000", ["AccentDangerBg"] = "#FF000000",
                    ["OverlayScrim"] = "#FF000000", ["MediaSurface"] = "#FF000000",
                    ["BadgeSurface"] = "#FF000000"
                },
                _ => new Dictionary<string, string>
                {
                    ["BgColor"] = "#201E1B", ["PanelSurface"] = "#2A2723",
                    ["PanelSurfaceStrong"] = "#332F2A", ["SurfaceElevated"] = "#3C3731",
                    ["PanelHighlight"] = "#463F37", ["BorderColor"] = "#42F4EBDD",
                    ["BorderSubtle"] = "#22F4EBDD", ["BorderActive"] = "#78F4EBDD",
                    ["TextMain"] = "#FFF8F0", ["TextSecondary"] = "#D8CDC0",
                    ["TextMuted"] = "#B2A69A", ["TextFaint"] = "#95897D",
                    ["TextOnAccent"] = "#25150F",
                    ["AccentPrimary"] = "#E5965B", ["AccentPrimaryHover"] = "#F0A66B",
                    ["AccentPrimaryPressed"] = "#C97945", ["AccentPrimaryBg"] = "#2EE5965B",
                    ["AccentCyan"] = "#E5965B", ["AccentCyanHover"] = "#F0A66B",
                    ["AccentCyanPressed"] = "#C97945", ["AccentCyanBg"] = "#2EE5965B",
                    ["AccentSafe"] = "#72B59A", ["AccentSafeBg"] = "#2472B59A",
                    ["AccentReview"] = "#E3B969", ["AccentReviewBg"] = "#26E3B969",
                    ["AccentInspect"] = "#B7A4E8", ["AccentInspectBg"] = "#26B7A4E8",
                    ["AccentDanger"] = "#F08A83", ["AccentDangerBg"] = "#28F08A83",
                    ["OverlayScrim"] = "#D9201E1B", ["MediaSurface"] = "#171614",
                    ["BadgeSurface"] = "#F02A2723"
                }
            };

            if (palette != "HighContrast")
            {
                foreach ((string key, string value) in GetPresetPalette(preset, palette == "Dark"))
                {
                    colors[key] = value;
                }
            }

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

        private static string NormalizeThemePreset(string preset)
        {
            string normalized = preset.Trim().ToLowerInvariant();
            return normalized == "azure" ? "sky" : normalized;
        }

        private static IReadOnlyDictionary<string, string> GetPresetPalette(string preset, bool dark)
        {
            (string paper, string paper2, string surface, string raised, string ink, string ink2,
             string muted, string faint, string rule, string ruleStrong, string primary,
             string primarySoft, string onPrimary, string accent, string accentSoft) values =
                (NormalizeThemePreset(preset), dark) switch
                {
                    ("sky", false) => ("#FFFFFF", "#E5E5E6", "#FFFFFF", "#F7F8F8", "#0F1419", "#29333B", "#52616C", "#70808A", "#D4DEE4", "#AEBDC6", "#1E9DF1", "#E3ECF6", "#07131B", "#1E9DF1", "#E3ECF6"),
                    ("sky", true) => ("#0B0D0F", "#191B1F", "#1D2125", "#111417", "#E7E9EA", "#DFE2E3", "#9B9FA3", "#747A80", "#272A2E", "#3B4249", "#1C9CF0", "#1A2129", "#07131B", "#1C9CF0", "#191F25"),
                    ("cobalt", false) => ("#FFFFFF", "#F5F5F5", "#FFFFFF", "#FFFFFF", "#262626", "#404040", "#666666", "#737373", "#BEBEBE", "#9F9F9F", "#1166D4", "#ECF4FD", "#FFFFFF", "#1166D4", "#EAEFF6"),
                    ("cobalt", true) => ("#1F1F1F", "#2E2E2E", "#262626", "#262626", "#EBEBEB", "#D9D9D9", "#A6A6A6", "#858585", "#525252", "#686868", "#80BBFF", "#003066", "#001833", "#80BBFF", "#333333"),
                    ("graphite", false) => ("#FCFCFC", "#F5F5F5", "#FCFCFC", "#FCFCFC", "#000000", "#292929", "#525252", "#747474", "#C4C4C4", "#A4A4A4", "#000000", "#EBEBEB", "#FFFFFF", "#FFAF09", "#FFF1CF"),
                    ("graphite", true) => ("#000000", "#1D1D1D", "#111111", "#000000", "#FFFFFF", "#E4E4E4", "#A4A4A4", "#747474", "#3D3D3D", "#525252", "#62A400", "#223900", "#000000", "#FFAF09", "#3B2A05"),
                    ("lagoon", false) => ("#F8FCFB", "#EDF3F2", "#FFFFFF", "#FFFFFF", "#173636", "#284B4B", "#58706F", "#718685", "#BFD3D0", "#9EB9B5", "#00B298", "#E0FAFF", "#001B18", "#00B298", "#E3FAFC"),
                    ("lagoon", true) => ("#050F0F", "#152828", "#102424", "#0A1A1A", "#F3F7F6", "#D7E4E2", "#9DB0AD", "#718C88", "#345050", "#496966", "#0DF2D0", "#133339", "#001B18", "#0DF2D0", "#173336"),
                    ("ink", false) => ("#F3F9FF", "#F5F5F5", "#F8FCFF", "#F8FCFF", "#000102", "#20252B", "#525252", "#747474", "#C7C7C7", "#A4A4A4", "#000102", "#EBEBEB", "#F3F9FF", "#2671F4", "#DCE9FF"),
                    ("ink", true) => ("#000000", "#1D1D1D", "#111114", "#08090B", "#C8D9F3", "#B7C8E1", "#A4A4A4", "#747474", "#3D3D3D", "#525252", "#C8D9F3", "#333333", "#000000", "#2671F4", "#13284A"),
                    ("ochre", false) => ("#FCF8F3", "#F3EBE2", "#FFFAF5", "#FAF5EF", "#17100B", "#312620", "#6A5A51", "#8A776B", "#E3D9CD", "#CBBCAE", "#F97015", "#F3EADE", "#25130A", "#F97015", "#F3EADE"),
                    ("ochre", true) => ("#0E0A07", "#231E1B", "#1A1512", "#252422", "#E7E4E2", "#C9C3C0", "#948984", "#827873", "#2B2522", "#423832", "#F97015", "#322A23", "#25130A", "#F97015", "#322A23"),
                    ("sepia", false) => ("#F9F9F9", "#EFEFEF", "#FCFCFC", "#FCFCFC", "#202020", "#343434", "#646464", "#7A7A7A", "#D8D8D8", "#B5B5B5", "#644A40", "#FFDFB5", "#FFFFFF", "#644A40", "#FFE6C4"),
                    ("sepia", true) => ("#111111", "#222222", "#191919", "#191919", "#EEEEEE", "#D8D5D2", "#B4B4B4", "#8E8E8E", "#302D29", "#484848", "#FFE0C2", "#393028", "#081A1B", "#FFE0C2", "#42382E"),
                    ("default", false) => ("#F8F8F6", "#EDE9DE", "#FFFFFF", "#FAF9F5", "#3D3929", "#535146", "#6F6D66", "#83827D", "#D7D7D5", "#B4B2A7", "#C96442", "#EFEEEB", "#25150F", "#C96442", "#E9E6DC"),
                    _ => ("#1F1F1E", "#1B1B19", "#30302E", "#262624", "#E5E5E2", "#C3C0B6", "#B7B5A9", "#8F8D84", "#3B3A39", "#52514A", "#D97757", "#3B2821", "#25150F", "#D97757", "#33251F")
                };

            return new Dictionary<string, string>
            {
                ["BgColor"] = values.paper,
                ["PanelSurfaceStrong"] = values.paper2,
                ["PanelSurface"] = values.surface,
                ["SurfaceElevated"] = values.raised,
                ["PanelHighlight"] = values.primarySoft,
                ["BorderSubtle"] = values.rule,
                ["BorderColor"] = values.ruleStrong,
                ["BorderActive"] = values.primary,
                ["TextMain"] = values.ink,
                ["TextSecondary"] = values.ink2,
                ["TextMuted"] = values.muted,
                ["TextFaint"] = values.faint,
                ["TextOnAccent"] = values.onPrimary,
                ["AccentPrimary"] = values.primary,
                ["AccentPrimaryHover"] = values.primary,
                ["AccentPrimaryPressed"] = values.primary,
                ["AccentPrimaryBg"] = values.primarySoft,
                ["AccentCyan"] = values.accent,
                ["AccentCyanHover"] = values.accent,
                ["AccentCyanPressed"] = values.accent,
                ["AccentCyanBg"] = values.accentSoft,
                ["AccentInspect"] = values.accent,
                ["AccentInspectBg"] = values.accentSoft,
                ["MediaSurface"] = values.paper2,
                ["BadgeSurface"] = values.surface,
                ["OverlayScrim"] = dark ? "#D9000000" : "#CCFFFFFF"
            };
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
            services.AddSingleton<MediaManagerTool>();
            services.AddSingleton<CleanerAssistantTool>();
            services.AddSingleton<BlueSapphire.Interfaces.IAIToolCapabilityProvider>(sp => sp.GetRequiredService<MediaManagerTool>());
            services.AddSingleton<BlueSapphire.Interfaces.IAIToolCapabilityProvider>(sp => sp.GetRequiredService<CleanerAssistantTool>());
            // ✅ 新增：注册我们的重命名业务服务 (使用 Singleton 单例即可，因为它是无状态的工具类)
            services.AddSingleton<BlueSapphire.Services.MediaRenameService>();

            // ✅ 新增：注册去重扫描业务服务
            services.AddSingleton<BlueSapphire.Services.MediaDeduplicationService>();

            // 注册 AI 服务
            services.AddSingleton<BlueSapphire.Services.DeepSeekAIService>();
            services.AddSingleton<BlueSapphire.Services.AIToolCapabilityCatalog>();
            services.AddSingleton<BlueSapphire.Services.AIToolsRegistry>();
            services.AddSingleton<BlueSapphire.Services.AIClassifierService>();
            services.AddSingleton<BlueSapphire.Services.AIChatHistoryService>();
            services.AddSingleton<BlueSapphire.Services.AITaskCenterService>();
            services.AddSingleton<BlueSapphire.Services.AISharedContextService>();
            services.AddSingleton<BlueSapphire.Services.AIPrivacyService>();
            services.AddSingleton<BlueSapphire.Services.AIOfflineIntentService>();
            services.AddSingleton<BlueSapphire.Services.AIMediaToolService>();
            services.AddSingleton<BlueSapphire.Services.MediaAIToolActionProvider>();
            services.AddSingleton<BlueSapphire.Interfaces.IAIToolActionProvider>(sp =>
                sp.GetRequiredService<BlueSapphire.Services.MediaAIToolActionProvider>());
            services.AddSingleton<BlueSapphire.Services.AIDiagnosticsService>();
            services.AddSingleton<BlueSapphire.Services.AICleanerRuleDraftService>();
            services.AddSingleton<BlueSapphire.Services.AIInsightService>();
            services.AddSingleton<BlueSapphire.Services.AIOperationPolicyService>();

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
            services.AddSingleton<BlueSapphire.Services.CleanerAIToolActionProvider>();
            services.AddSingleton<BlueSapphire.Interfaces.IAIToolActionProvider>(sp =>
                sp.GetRequiredService<BlueSapphire.Services.CleanerAIToolActionProvider>());
            services.AddSingleton<BlueSapphire.Services.CleanerStateStore>();
            services.AddSingleton<BlueSapphire.Services.CleanerOperationCoordinator>();
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
            services.AddSingleton<BlueSapphire.Services.CleanerSystemCleanupService>();
            services.AddSingleton<BlueSapphire.Services.CleanerApplicationDiscoveryService>();
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
            ApplyUiFontSize(AppSettings.Get("UiFontSize", "standard") ?? "standard");
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
