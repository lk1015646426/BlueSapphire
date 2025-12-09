using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System.Numerics;
using System;
using System.Collections.Generic;
using Windows.UI;
using System.Reflection;
using System.Linq;
using BlueSapphire.Interfaces;
using BlueSapphire.Helpers; // 【必须引用】新增的 Helpers
using Microsoft.UI;
using Microsoft.UI.Windowing;

// 注意：移除了 Windows.Storage，因为 Unpackaged 模式下不需要它了

namespace BlueSapphire
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; } = null!;
        public bool IsParticleEffectEnabled { get; private set; } = true;

        private List<ITool> _tools = new List<ITool>();
        private List<Particle> _particles = new List<Particle>();
        private Random _random = new Random();
        private Vector2 _mousePosition;
        private const int ParticleCount = 100;
        private const float ConnectionDistance = 150f;

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            // 1. 读取配置 (使用新的 AppSettings 类)
            LoadSettingsFromDisk();

            // 2. 自定义标题栏
            if (AppTitleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
            }
            CustomizeTitleBar();

            // 3. 加载插件
            LoadTools();
            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }

        // --- 核心配置逻辑 (修改版) ---

        private void LoadSettingsFromDisk()
        {
            // 使用自定义的 AppSettings 替代 ApplicationData
            // 如果读取不到，Get 方法会自动返回 true (我们设定的默认值)
            IsParticleEffectEnabled = AppSettings.Get<bool>("IsParticleEffectEnabled", true);
        }

        // 提供给外部调用的更新方法
        public void UpdateParticleSetting(bool isEnabled)
        {
            IsParticleEffectEnabled = isEnabled;

            // 如果是关闭，强制刷新一次画布
            if (BackgroundCanvas != null)
            {
                BackgroundCanvas.Invalidate();
            }
        }
        // -----------------------

        private void CustomizeTitleBar()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 0, 255, 255);
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(80, 0, 255, 255);
                titleBar.ButtonInactiveForegroundColor = Colors.Gray;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }
        }

        private void LoadTools()
        {
            try
            {
                var interfaceType = typeof(ITool);
                var types = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is ITool tool)
                    {
                        tool.Initialize();
                        _tools.Add(tool);

                        var navItem = new NavigationViewItem
                        {
                            Content = tool.Title,
                            Icon = new SymbolIcon(tool.Icon),
                            Tag = tool.Id
                        };
                        NavView.MenuItems.Add(navItem);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"插件加载失败: {ex.Message}");
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                if (args.SelectedItem is NavigationViewItem selectedItem &&
                    selectedItem.Tag?.ToString() is string tag)
                {
                    var targetTool = _tools.FirstOrDefault(t => t.Id == tag);
                    if (targetTool != null)
                    {
                        ContentFrame.Navigate(targetTool.ContentPage);
                    }
                }
            }
        }

        private void OnCreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
            _particles.Clear();
            for (int i = 0; i < ParticleCount; i++)
            {
                _particles.Add(new Particle((float)sender.ActualWidth, (float)sender.ActualHeight, _random));
            }
        }

        private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!sender.IsLoaded) return;

            if (!IsParticleEffectEnabled) return;

            var session = args.DrawingSession;
            foreach (var p in _particles)
            {
                p.Update((float)sender.ActualWidth, (float)sender.ActualHeight, _mousePosition);
            }

            for (int i = 0; i < _particles.Count; i++)
            {
                for (int j = i + 1; j < _particles.Count; j++)
                {
                    var p1 = _particles[i];
                    var p2 = _particles[j];
                    var dist = Vector2.Distance(p1.Position, p2.Position);

                    if (dist < ConnectionDistance)
                    {
                        float alpha = 1.0f - (dist / ConnectionDistance);
                        session.DrawLine(p1.Position, p2.Position,
                            Color.FromArgb((byte)(alpha * 100), 0, 255, 255), 1);
                    }
                }
            }

            foreach (var p in _particles)
            {
                session.FillCircle(p.Position, 2, Colors.Cyan);
            }

            try
            {
                sender.Invalidate();
            }
            catch (Exception)
            {
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                var point = e.GetCurrentPoint(element);
                _mousePosition = new Vector2((float)point.Position.X, (float)point.Position.Y);
            }
        }
    }
}