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
using BlueSapphire.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
// 必须保留这个引用，用于 AOT 兼容性
using System.Diagnostics.CodeAnalysis;

namespace BlueSapphire
{
    public sealed partial class MainWindow : Window
    {
        // [核心修复] 补回 Instance，供 MediaManagerPage 调用
        public static MainWindow Instance { get; private set; } = null!;
        public bool IsParticleEffectEnabled { get; private set; } = true;

        // [核心修复] 补回 _tools 定义
        private List<ITool> _tools = new List<ITool>();
        private List<Particle> _particles = new List<Particle>();
        private Random _random = new Random();

        // 将默认鼠标位置移出屏幕，防止左上角粒子被排斥
        private Vector2 _mousePosition = new Vector2(-1000, -1000);

        private const int ParticleCount = 100;
        private const float ConnectionDistance = 150f;

        // 网格分区数据结构
        private Dictionary<long, List<Particle>> _grid = new Dictionary<long, List<Particle>>();
        private int _gridCellSize = (int)ConnectionDistance;

        public MainWindow()
        {
            this.InitializeComponent();
            // [核心修复] 在构造函数中赋值 Instance
            Instance = this;
            LoadSettingsFromDisk();

            if (AppTitleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
            }
            CustomizeTitleBar();
            LoadTools();
            if (NavView.MenuItems.Count > 0) NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void LoadSettingsFromDisk()
        {
            IsParticleEffectEnabled = AppSettings.Get<bool>("IsParticleEffectEnabled", true);
        }

        public void UpdateParticleSetting(bool isEnabled)
        {
            IsParticleEffectEnabled = isEnabled;
            if (BackgroundCanvas != null) BackgroundCanvas.Invalidate();
        }

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
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 0, 255, 255);
            }
        }

        // [AOT 适配] 防止插件类被裁剪
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(HomePage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(MediaManagerPage))]
        private void LoadTools()
        {
            try
            {
                var interfaceType = typeof(ITool);
                // .NET 10 下的反射加载
                var types = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is ITool tool)
                    {
                        tool.Initialize();
                        _tools.Add(tool); // 这里现在能找到 _tools 了
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
            if (args.IsSettingsSelected) ContentFrame.Navigate(typeof(SettingsPage));
            else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                var tool = _tools.FirstOrDefault(t => t.Id == tag);
                if (tool != null) ContentFrame.Navigate(tool.ContentPage);
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
            if (!sender.IsLoaded || !IsParticleEffectEnabled) return;

            var session = args.DrawingSession;
            float width = (float)sender.ActualWidth;
            float height = (float)sender.ActualHeight;

            // 1. 更新粒子 并 重建网格
            _grid.Clear();
            _gridCellSize = 150;

            foreach (var p in _particles)
            {
                p.Update(width, height, _mousePosition);

                int cellX = (int)(p.Position.X / _gridCellSize);
                int cellY = (int)(p.Position.Y / _gridCellSize);
                long key = ((long)cellX << 32) | (uint)cellY;

                if (!_grid.ContainsKey(key)) _grid[key] = new List<Particle>();
                _grid[key].Add(p);
            }

            // 2. 绘制连线 (高性能网格分区算法)
            foreach (var kvp in _grid)
            {
                long key = kvp.Key;
                int cellX = (int)(key >> 32);
                int cellY = (int)(key & 0xFFFFFFFF);
                var cellParticles = kvp.Value;

                // 检查周围九宫格
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = cellX + dx;
                        int ny = cellY + dy;
                        long neighborKey = ((long)nx << 32) | (uint)ny;

                        if (_grid.TryGetValue(neighborKey, out var neighborParticles))
                        {
                            foreach (var p1 in cellParticles)
                            {
                                foreach (var p2 in neighborParticles)
                                {
                                    if (p1 == p2) continue;

                                    var distSq = Vector2.DistanceSquared(p1.Position, p2.Position);
                                    if (distSq < ConnectionDistance * ConnectionDistance)
                                    {
                                        float dist = (float)Math.Sqrt(distSq);
                                        float alpha = 1.0f - (dist / ConnectionDistance);
                                        session.DrawLine(p1.Position, p2.Position,
                                            Color.FromArgb((byte)(alpha * 100), 0, 255, 255), 1);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 3. 绘制粒子点
            foreach (var p in _particles)
            {
                session.FillCircle(p.Position, 2, Colors.Cyan);
            }

            sender.Invalidate();
        }
    }
}