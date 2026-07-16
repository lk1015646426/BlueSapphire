using BlueSapphire.Helpers;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;
using BlueSapphire.Tools;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading;
using Windows.UI;

namespace BlueSapphire
{
    public sealed partial class MainWindow : Window
    {

        public bool IsParticleEffectEnabled { get; private set; } = true;

        public System.Collections.Generic.IReadOnlyList<ITool> Tools => _tools;
        private List<ITool> _tools = new List<ITool>();
        private System.Collections.ObjectModel.ObservableCollection<NavigationViewItem> _navItems = new System.Collections.ObjectModel.ObservableCollection<NavigationViewItem>();
        private List<Particle> _particles = new List<Particle>();
        private Random _random = new Random();
        private Vector2 _mousePosition = new Vector2(-1000, -1000);

        private const float ConnectionDistance = 150f;
        private const float ConnectionDistanceSq = ConnectionDistance * ConnectionDistance;
        private static readonly (int X, int Y)[] ForwardNeighborOffsets =
        {
            (0, 1), (1, -1), (1, 0), (1, 1)
        };

        private const int MaxGridCols = 40;
        private const int MaxGridRows = 30;
        private List<Particle>[,] _gridArray;
        private int _currentCols = 0;
        private int _currentRows = 0;

        private int _gridCellSize = (int)ConnectionDistance;

        private Color[] _alphaColors;

        // 粒子渲染：基于时间的移动（delta time），速度与帧率无关。
        // 窗口失活/最小化时暂停渲染以释放 CPU/GPU。
        private bool _isWindowActive = true;
        private readonly Stopwatch _renderStopwatch = Stopwatch.StartNew();
        private float _lastRenderTimeSeconds;
        private readonly SemaphoreSlim _dialogGate = new(1, 1);

        public MainWindow()
        {
            this.InitializeComponent();

            NavView.MenuItemsSource = _navItems;

            _gridArray = new List<Particle>[MaxGridCols, MaxGridRows];
            for (int i = 0; i < MaxGridCols; i++)
            {
                for (int j = 0; j < MaxGridRows; j++)
                {
                    _gridArray[i, j] = new List<Particle>(16);
                }
            }

            LoadSettingsFromDisk();

            _alphaColors = new Color[101];
            for (int i = 0; i <= 100; i++)
            {
                _alphaColors[i] = Color.FromArgb((byte)i, 34, 211, 238);
            }

            if (AppTitleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
            }
            CustomizeTitleBar();
            LoadTools();

            // 设置窗口最小尺寸，确保左右布局在小窗口下不崩
            SetWindowMinSize(960, 640);

            // 窗口失活（含最小化/切到其他窗口）时暂停粒子渲染，恢复时立即续帧。
            this.Activated += OnWindowActivated;

            WeakReferenceMessenger.Default.Register<ToggleParticleMessage>(this, (r, m) =>
            {
                IsParticleEffectEnabled = m.Value;

                // [核心修复] 移除原来的 if 限制。
                // 无论开启还是关闭，状态切换时都必须强制重绘一次画布。
                // 如果是关闭，这最后一次重绘会让画布执行下方的 OnDraw 逻辑并清空画面，彻底消灭残留的粒子。
                BackgroundCanvas?.Invalidate();
            });

            WeakReferenceMessenger.Default.Register<ShowTipMessage>(this, async (r, m) =>
            {
                await _dialogGate.WaitAsync();
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = m.Title,
                        Content = m.Message,
                        CloseButtonText = "确定",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                catch
                {
                    // 页面切换或窗口关闭时不再展示过期提示。
                }
                finally
                {
                    _dialogGate.Release();
                }
            });

            SelectInitialTool();
            CompositionTarget.Rendering += OnRendering;
            Closed += MainWindow_Closed;
        }

        private void OnRendering(object? sender, object e)
        {
            if (!IsParticleEffectEnabled || BackgroundCanvas == null) return;

            // 窗口失活（最小化/切走）时完全暂停：既不更新逻辑也不重绘，释放 CPU/GPU。
            if (!_isWindowActive) return;

            // 基于时间的移动：计算自上一帧以来的实际经过时间
            float currentTime = (float)_renderStopwatch.Elapsed.TotalSeconds;
            float deltaTime = currentTime - _lastRenderTimeSeconds;
            _lastRenderTimeSeconds = currentTime;

            // 防止窗口恢复或卡顿后的大跳变
            if (deltaTime > 0.1f) deltaTime = 0.1f;
            if (deltaTime <= 0f) deltaTime = 0.016f;

            UpdateLogic(deltaTime);
            BackgroundCanvas.Invalidate();
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            bool nowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            if (nowActive == _isWindowActive) return;

            _isWindowActive = nowActive;

            if (nowActive)
            {
                // 重置渲染计时基准，避免恢复后 delta time 过大导致粒子瞬移
                _lastRenderTimeSeconds = (float)_renderStopwatch.Elapsed.TotalSeconds;
            }

            BackgroundCanvas?.Invalidate();
        }

        private void LoadSettingsFromDisk()
        {
            IsParticleEffectEnabled =
                AppSettings.Get<bool>("IsParticleEffectEnabled", true) &&
                !AppSettings.Get("ReduceMotion", false);
        }

        private void CustomizeTitleBar()
        {
            ApplyThemeChrome(ElementTheme.Dark);
        }

        public void ApplyThemeChrome(ElementTheme theme)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ButtonForegroundColor = GetResourceColor("TextMain", Colors.White);
                titleBar.ButtonInactiveForegroundColor = GetResourceColor("TextMuted", Colors.Gray);
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonHoverBackgroundColor = GetResourceColor(
                    "AccentCyanBg",
                    theme == ElementTheme.Light
                        ? Color.FromArgb(24, 8, 124, 147)
                        : Color.FromArgb(40, 34, 211, 238));
                titleBar.ButtonHoverForegroundColor = GetResourceColor("AccentCyan", Colors.Cyan);
            }
        }

        private static Color GetResourceColor(string key, Color fallback)
        {
            return Application.Current.Resources.TryGetValue(key, out object? value) &&
                   value is SolidColorBrush brush
                ? brush.Color
                : fallback;
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(HomePage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(MediaManagerPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CleanerAssistantPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.DevLogPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.AICopilotPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(AboutPage))]
        private void LoadTools()
        {
            RegisterTool(App.Current.Services.GetRequiredService<HomeTool>());
            RegisterTool(App.Current.Services.GetRequiredService<AICopilotTool>());
            RegisterTool(App.Current.Services.GetRequiredService<MediaManagerTool>());
            RegisterTool(App.Current.Services.GetRequiredService<CleanerAssistantTool>());
            RegisterTool(App.Current.Services.GetRequiredService<DevLogTool>());
            RegisterTool(App.Current.Services.GetRequiredService<AboutTool>());
        }

        private void RegisterTool(ITool tool)
        {
            tool.Initialize();
            _tools.Add(tool);
            var navItem = new NavigationViewItem
            {
                Content = tool.Title,
                Icon = new SymbolIcon(tool.Icon),
                Tag = tool.Id
            };

            _navItems.Add(navItem);
        }





        private static T? FindVisualChild<T>(DependencyObject parent, string name = "") where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && (string.IsNullOrEmpty(name) || typedChild.Name == name))
                    return typedChild;

                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            Type? targetPage = null;

            if (args.IsSettingsSelected)
                targetPage = typeof(SettingsPage);
            else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                var tool = _tools.FirstOrDefault(t => t.Id == tag);
                if (tool != null)
                    targetPage = tool.ContentPage;
            }

            if (targetPage != null)
            {
                ContentFrame.Navigate(targetPage);

                // P2: 粒子背景仅在主页（氛围页）显示，功能页面隐藏以降低 GPU 负载和视觉干扰
                bool isAmbientPage = targetPage == typeof(HomePage);
                if (BackgroundCanvas != null)
                {
                    BackgroundCanvas.Visibility = isAmbientPage ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        public void NavigateToTool(string tag)
        {
            if (string.Equals(tag, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            var item = _navItems.FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                NavView.SelectedItem = item;
            }
        }

        private void SelectInitialTool()
        {
            string? requestedToolId = ParseRequestedToolId(App.LaunchArguments);
            if (!string.IsNullOrWhiteSpace(requestedToolId))
            {
                NavigationViewItem? match = _navItems
                    .FirstOrDefault(item => string.Equals(item.Tag as string, requestedToolId, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    NavView.SelectedItem = match;
                    return;
                }
            }

            if (_navItems.Count > 0)
            {
                NavView.SelectedItem = _navItems[0];
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

        private void OnCreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
            _particles.Clear();
            for (int i = 0; i < 100; i++)
            {
                _particles.Add(new Particle((float)sender.ActualWidth, (float)sender.ActualHeight, _random));
            }
        }

        private void UpdateLogic(float deltaTime)
        {
            if (BackgroundCanvas == null) return;
            if (BackgroundCanvas.ActualWidth <= 0 || BackgroundCanvas.ActualHeight <= 0) return;

            float width = (float)BackgroundCanvas.ActualWidth;
            float height = (float)BackgroundCanvas.ActualHeight;

            _currentCols = Math.Min((int)(width / _gridCellSize) + 1, MaxGridCols);
            _currentRows = Math.Min((int)(height / _gridCellSize) + 1, MaxGridRows);

            for (int i = 0; i < _currentCols; i++)
            {
                for (int j = 0; j < _currentRows; j++)
                {
                    _gridArray[i, j].Clear();
                }
            }

            foreach (var p in _particles)
            {
                p.Update(width, height, _mousePosition, deltaTime);

                int cellX = (int)(p.Position.X / _gridCellSize);
                int cellY = (int)(p.Position.Y / _gridCellSize);

                if (cellX >= 0 && cellX < _currentCols && cellY >= 0 && cellY < _currentRows)
                {
                    _gridArray[cellX, cellY].Add(p);
                }
            }
        }

        private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!IsParticleEffectEnabled)
            {
                // [核心修复] 双重保险：当粒子效果被关闭时，显式调用一次 Clear 清空画布。
                // 这样彻底杜绝了最后一帧残留导致的“暂停”错觉。
                args.DrawingSession.Clear(Colors.Transparent);
                return;
            }

            var session = args.DrawingSession;

            void DrawConnection(Particle p1, Particle p2)
            {
                float distSq = Vector2.DistanceSquared(p1.Position, p2.Position);
                if (distSq >= ConnectionDistanceSq) return;

                float alpha = 1.0f - (float)Math.Sqrt(distSq) / ConnectionDistance;
                int index = Math.Clamp((int)(alpha * 100), 0, 100);
                session.DrawLine(p1.Position, p2.Position, _alphaColors[index], 1);
            }

            for (int cellX = 0; cellX < _currentCols; cellX++)
            {
                for (int cellY = 0; cellY < _currentRows; cellY++)
                {
                    var cellParticles = _gridArray[cellX, cellY];
                    if (cellParticles.Count == 0) continue;

                    for (int first = 0; first < cellParticles.Count; first++)
                    {
                        for (int second = first + 1; second < cellParticles.Count; second++)
                        {
                            DrawConnection(cellParticles[first], cellParticles[second]);
                        }
                    }

                    foreach ((int offsetX, int offsetY) in ForwardNeighborOffsets)
                    {
                        int neighborX = cellX + offsetX;
                        int neighborY = cellY + offsetY;
                        if (neighborX < 0 || neighborX >= _currentCols ||
                            neighborY < 0 || neighborY >= _currentRows)
                        {
                            continue;
                        }

                        foreach (Particle first in cellParticles)
                        {
                            foreach (Particle second in _gridArray[neighborX, neighborY])
                            {
                                DrawConnection(first, second);
                            }
                        }
                    }
                }
            }

            foreach (var p in _particles)
            {
                session.FillCircle(p.Position, 2, Color.FromArgb(255, 34, 211, 238));
            }
        }

        private void BackgroundCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint((UIElement)sender);
            _mousePosition = new Vector2((float)ptr.Position.X, (float)ptr.Position.Y);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            CompositionTarget.Rendering -= OnRendering;
            Activated -= OnWindowActivated;
            Closed -= MainWindow_Closed;
            WeakReferenceMessenger.Default.UnregisterAll(this);

            if (_hWnd != IntPtr.Zero && _subclassProc != null)
            {
                IntPtr callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_subclassProc);
                RemoveWindowSubclass(_hWnd, callback, _subclassId);
            }

            try
            {
                BackgroundCanvas.RemoveFromVisualTree();
            }
            catch
            {
                // 窗口正在销毁时画布可能已经由 WinUI 释放。
            }

            (App.Current.Services as IDisposable)?.Dispose();
        }

        #region Window Min Size

        private const int WM_GETMINMAXINFO = 0x0024;
        private MINMAXINFO _minMaxInfo;
        private bool _minSizeSet;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public PointStruct reserved;
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

            public PointStruct(int x, int y) { X = x; Y = y; }
        }

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern IntPtr SetWindowSubclass(IntPtr hWnd, IntPtr pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, IntPtr pfnSubclass, UIntPtr uIdSubclass);

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

        private SubclassProc? _subclassProc;
        private IntPtr _hWnd;
        private static readonly UIntPtr _subclassId = (UIntPtr)1;

        private void SetWindowMinSize(int minWidth, int minHeight)
        {
            _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _minMaxInfo.MinTrackSize = new PointStruct(minWidth, minHeight);
            _minSizeSet = true;

            _subclassProc = new SubclassProc(WindowSubclassProc);
            var funcPtr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_subclassProc);
            SetWindowSubclass(_hWnd, funcPtr, _subclassId, IntPtr.Zero);
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_GETMINMAXINFO && _minSizeSet)
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.MinTrackSize = _minMaxInfo.MinTrackSize;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                return IntPtr.Zero;
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        #endregion
    }
}
