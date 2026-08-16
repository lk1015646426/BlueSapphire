using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Tools;
using BlueSapphire.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Windows.UI;

namespace BlueSapphire
{
    public sealed partial class MainWindow : Window
    {
        public IReadOnlyList<ITool> Tools => _tools;

        private readonly List<ITool> _tools = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<object> _navItems = new();
        private readonly SemaphoreSlim _dialogGate = new(1, 1);
        private object? _lastNavigationItem;
        private int _shutdownStarted;

        public MainWindow()
        {
            InitializeComponent();
            Title = "BlueSapphire";
            NavView.MenuItemsSource = _navItems;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            CustomizeTitleBar();
            TryApplySystemBackdrop();
            LoadTools();

            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.Content = "设置";
            }

            UpdateThemeToggleLabel();
            SetWindowMinSize(840, 600);

            WeakReferenceMessenger.Default.Register<ShowTipMessage>(this, async (_, message) =>
            {
                await _dialogGate.WaitAsync();
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = message.Title,
                        Content = message.Message,
                        CloseButtonText = "确定",
                        XamlRoot = Content.XamlRoot,
                        Background = AcrylicHelper.CreatePanelAcrylic(),
                        CornerRadius = new CornerRadius(8)
                    };
                    await dialog.ShowAsync();
                }
                catch
                {
                    // 页面切换或窗口关闭时忽略已经过期的提示。
                }
                finally
                {
                    _dialogGate.Release();
                }
            });

            SelectInitialTool();
            Closed += MainWindow_Closed;
        }

        private void CustomizeTitleBar() => ApplyThemeChrome(ElementTheme.Dark);

        private void TryApplySystemBackdrop()
        {
            Brush fallback = GetResourceBrush(
                "BgColor",
                new SolidColorBrush(Color.FromArgb(255, 31, 31, 30)));

            try
            {
                SystemBackdrop = new MicaBackdrop();
                RootLayout.Background = new SolidColorBrush(Colors.Transparent);
                NavView.Background = new SolidColorBrush(Colors.Transparent);
            }
            catch
            {
                // Windows 版本、远程会话或图形策略不支持材质时，保持普通主题背景。
                RootLayout.Background = GetResourceBrush("BgColor", fallback);
                NavView.Background = GetResourceBrush("BgColor", fallback);
            }
        }

        private static Brush GetResourceBrush(string key, Brush fallback) =>
            Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
                ? brush
                : fallback;

        public void ApplyThemeChrome(ElementTheme theme)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            AppWindowTitleBar titleBar = appWindow.TitleBar;
            titleBar.ButtonForegroundColor = GetResourceColor("TextMain", Colors.White);
            titleBar.ButtonInactiveForegroundColor = GetResourceColor("TextMuted", Colors.Gray);
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = GetResourceColor(
                "AccentPrimaryBg",
                theme == ElementTheme.Light
                    ? Color.FromArgb(24, 201, 100, 66)
                    : Color.FromArgb(40, 217, 119, 87));
            titleBar.ButtonHoverForegroundColor = GetResourceColor("AccentPrimary", Color.FromArgb(255, 217, 119, 87));
        }

        private static Color GetResourceColor(string key, Color fallback) =>
            Application.Current.Resources.TryGetValue(key, out object? value) && value is SolidColorBrush brush
                ? brush.Color
                : fallback;

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(HomePage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(MediaManagerPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CleanerAssistantPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.DevLogPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.AICopilotPage))]
        private void LoadTools()
        {
            _navItems.Add(new NavigationViewItemHeader { Content = "工作区" });
            RegisterTool(App.Current.Services.GetRequiredService<HomeTool>());
            RegisterTool(App.Current.Services.GetRequiredService<MediaManagerTool>());
            RegisterTool(App.Current.Services.GetRequiredService<CleanerAssistantTool>());
        }

        private void RegisterTool(ITool tool)
        {
            tool.Initialize();
            _tools.Add(tool);
            _navItems.Add(new NavigationViewItem
            {
                Content = tool.Title,
                Icon = new SymbolIcon(tool.Icon),
                Tag = tool.Id
            });
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            Type? targetPage = null;

            if (args.IsSettingsSelected)
            {
                targetPage = typeof(SettingsPage);
                _lastNavigationItem = NavView.SettingsItem;
            }
            else if (args.SelectedItem is NavigationViewItem { Tag: string tag } item)
            {
                if (string.Equals(tag, "ThemeToggle", StringComparison.Ordinal))
                {
                    ToggleTheme();
                    if (_lastNavigationItem != null)
                    {
                        NavView.SelectedItem = _lastNavigationItem;
                    }
                    return;
                }

                if (string.Equals(tag, "DevLog", StringComparison.Ordinal))
                {
                    targetPage = typeof(Views.DevLogPage);
                    _lastNavigationItem = item;
                }
                else
                {
                    ITool? tool = _tools.FirstOrDefault(candidate => candidate.Id == tag);
                    if (tool != null)
                    {
                        targetPage = tool.ContentPage;
                        _lastNavigationItem = item;
                    }
                }
            }

            if (targetPage != null && ContentFrame.CurrentSourcePageType != targetPage)
            {
                ContentFrame.Navigate(targetPage);
            }
        }

        private void ToggleTheme()
        {
            string next = RootLayout.ActualTheme == ElementTheme.Dark ? "Light" : "Dark";
            AppSettings.Save("AppTheme", next);
            App.ApplyThemePreference(next);
            UpdateThemeToggleLabel();
        }

        private void UpdateThemeToggleLabel()
        {
            bool isDark = RootLayout.ActualTheme == ElementTheme.Dark;
            ThemeToggleItem.Content = isDark ? "浅色模式" : "深色模式";
            if (ThemeToggleItem.Icon is FontIcon icon)
            {
                icon.Glyph = isDark ? "\uE706" : "\uE708";
            }
        }

        public void NavigateToTool(string tag)
        {
            if (string.Equals(tag, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            NavigationViewItem? item = _navItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                NavView.SelectedItem = item;
            }
        }

        public void NavigateToDevLogPage() => ContentFrame.Navigate(typeof(Views.DevLogPage));

        private void SelectInitialTool()
        {
            string? requestedToolId = ParseRequestedToolId(App.LaunchArguments);
            if (string.Equals(requestedToolId, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                _lastNavigationItem = NavView.SettingsItem;
                ContentFrame.Navigate(typeof(SettingsPage));
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            if (string.Equals(requestedToolId, "DevLog", StringComparison.OrdinalIgnoreCase))
            {
                NavigationViewItem? devLogItem = NavView.FooterMenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, "DevLog", StringComparison.Ordinal));
                if (devLogItem != null)
                {
                    NavView.SelectedItem = devLogItem;
                    return;
                }
            }

            NavigationViewItem? item = !string.IsNullOrWhiteSpace(requestedToolId)
                ? _navItems.OfType<NavigationViewItem>().FirstOrDefault(candidate =>
                    string.Equals(candidate.Tag as string, requestedToolId, StringComparison.OrdinalIgnoreCase))
                : null;

            item ??= _navItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (item != null)
            {
                NavView.SelectedItem = item;
            }
        }

        private static string? ParseRequestedToolId(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return null;
            }

            foreach (string token in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("--tool=", StringComparison.OrdinalIgnoreCase))
                {
                    return token["--tool=".Length..].Trim();
                }
            }

            return null;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            {
                return;
            }

            Closed -= MainWindow_Closed;
            WeakReferenceMessenger.Default.UnregisterAll(this);

            // 先取消当前页面仍在执行的磁盘、网络或媒体任务，避免退出期间继续回调已销毁的 UI。
            try
            {
                switch (ContentFrame.Content)
                {
                    case CleanerAssistantPage cleanerPage:
                        cleanerPage.ViewModel.Shutdown();
                        break;
                    case MediaManagerPage mediaPage:
                        mediaPage.ViewModel.CancelPendingOperations();
                        break;
                }
            }
            catch
            {
                // 退出流程必须继续，单个页面的清理失败不能阻止窗口关闭。
            }

            if (_hWnd != IntPtr.Zero && _subclassProc != null)
            {
                IntPtr callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_subclassProc);
                RemoveWindowSubclass(_hWnd, callback, _subclassId);
            }

            try
            {
                (App.Current.Services as IDisposable)?.Dispose();
            }
            finally
            {
                Application.Current.Exit();
            }
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private MINMAXINFO _minMaxInfo;
        private bool _minSizeSet;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public PointStruct Reserved;
            public PointStruct MaxSize;
            public PointStruct MaxPosition;
            public PointStruct MinTrackSize;
            public PointStruct MaxTrackSize;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PointStruct
        {
            public int X;
            public int Y;

            public PointStruct(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern IntPtr SetWindowSubclass(IntPtr hWnd, IntPtr procedure, UIntPtr subclassId, IntPtr referenceData);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, IntPtr procedure, UIntPtr subclassId);

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate IntPtr SubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData);

        private SubclassProc? _subclassProc;
        private IntPtr _hWnd;
        private static readonly UIntPtr _subclassId = (UIntPtr)1;

        private void SetWindowMinSize(int minWidth, int minHeight)
        {
            _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _minMaxInfo.MinTrackSize = new PointStruct(minWidth, minHeight);
            _minSizeSet = true;

            _subclassProc = WindowSubclassProc;
            IntPtr functionPointer = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_subclassProc);
            SetWindowSubclass(_hWnd, functionPointer, _subclassId, IntPtr.Zero);
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData)
        {
            if (message == WM_GETMINMAXINFO && _minSizeSet)
            {
                MINMAXINFO info = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.MinTrackSize = _minMaxInfo.MinTrackSize;
                System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, true);
                return IntPtr.Zero;
            }

            return DefSubclassProc(hWnd, message, wParam, lParam);
        }
    }
}
