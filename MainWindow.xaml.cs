using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System.Numerics;
using System;
using System.Collections.Generic;
using Windows.UI;
using System.Linq;
using BlueSapphire.Interfaces;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Media; // 引用 CompositionTarget
// [AOT 兼容]
using System.Diagnostics.CodeAnalysis;

namespace BlueSapphire
{
    public sealed partial class MainWindow : Window
    {
        public bool IsParticleEffectEnabled { get; private set; } = true;

        private List<ITool> _tools = new List<ITool>();
        private List<Particle> _particles = new List<Particle>();
        private Random _random = new Random();
        private Vector2 _mousePosition = new Vector2(-1000, -1000);

        // [优化] 预计算
        private const float ConnectionDistance = 150f;
        private const float ConnectionDistanceSq = ConnectionDistance * ConnectionDistance;

        // [优化] 对象池 (Zero-Allocation)
        private Dictionary<long, List<Particle>> _grid = new Dictionary<long, List<Particle>>();
        private Stack<List<Particle>> _listPool = new Stack<List<Particle>>();
        private int _gridCellSize = (int)ConnectionDistance;

        public MainWindow()
        {
            this.InitializeComponent();
            LoadSettingsFromDisk();

            if (AppTitleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
            }
            CustomizeTitleBar();
            LoadTools();

            // 注册消息
            WeakReferenceMessenger.Default.Register<ToggleParticleMessage>(this, (r, m) =>
            {
                IsParticleEffectEnabled = m.Value;
                // 如果禁用，可以手动重绘一次清空屏幕，或者停止 invalidation
                if (IsParticleEffectEnabled) BackgroundCanvas?.Invalidate();
            });

            // 启动时默认选中第一个 (即 HomePage)
            if (NavView.MenuItems.Count > 0) NavView.SelectedItem = NavView.MenuItems[0];

            // [关键修复] 手动注册渲染循环，替代 CanvasAnimatedControl
            CompositionTarget.Rendering += OnRendering;
        }

        // [关键修复] 手动游戏循环
        private void OnRendering(object? sender, object e)
        {
            if (!IsParticleEffectEnabled || BackgroundCanvas == null) return;

            // 1. 在这里执行逻辑更新 (原本的 OnUpdate)
            UpdateLogic();

            // 2. 触发重绘 (原本的 OnDraw 会被调用)
            BackgroundCanvas.Invalidate();
        }

        private void LoadSettingsFromDisk()
        {
            IsParticleEffectEnabled = AppSettings.Get<bool>("IsParticleEffectEnabled", true);
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

        // --- 👇 重点修改了这里 👇 ---
        // [AOT 适配] 显式告诉编译器保留这两个页面的构造函数
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(HomePage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(MediaManagerPage))]
        private void LoadTools()
        {
            // 1. 注册首页 (放在第一个，启动时会默认选中并显示它)
            RegisterTool(new HomePage());

            // 2. 注册功能页
            RegisterTool(new MediaManagerPage());
        }
        // ---------------------------

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
            NavView.MenuItems.Add(navItem);
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
            for (int i = 0; i < 100; i++)
            {
                _particles.Add(new Particle((float)sender.ActualWidth, (float)sender.ActualHeight, _random));
            }
        }

        // 逻辑更新 (独立出来)
        private void UpdateLogic()
        {
            if (BackgroundCanvas == null) return;

            // [新增] 安全检查：如果宽高无效，直接跳过，防止崩溃
            if (BackgroundCanvas.ActualWidth <= 0 || BackgroundCanvas.ActualHeight <= 0) return;

            float width = (float)BackgroundCanvas.ActualWidth;
            float height = (float)BackgroundCanvas.ActualHeight;

            // 回收 List
            foreach (var list in _grid.Values)
            {
                list.Clear();
                _listPool.Push(list);
            }
            _grid.Clear();

            // 更新粒子 & 重建网格
            foreach (var p in _particles)
            {
                p.Update(width, height, _mousePosition);

                int cellX = (int)(p.Position.X / _gridCellSize);
                int cellY = (int)(p.Position.Y / _gridCellSize);
                long key = ((long)cellX << 32) | (uint)cellY;

                if (!_grid.TryGetValue(key, out var cellList))
                {
                    cellList = _listPool.Count > 0 ? _listPool.Pop() : new List<Particle>(16);
                    _grid[key] = cellList;
                }
                cellList.Add(p);
            }
        }

        // 绘制方法 (由 Invalidate 触发)
        private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!IsParticleEffectEnabled) return;

            var session = args.DrawingSession;

            // 绘制连线
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
                        long neighborKey = ((long)(cellX + dx) << 32) | (uint)(cellY + dy);
                        if (_grid.TryGetValue(neighborKey, out var neighborParticles))
                        {
                            foreach (var p1 in cellParticles)
                            {
                                foreach (var p2 in neighborParticles)
                                {
                                    if (p1 == p2) continue;

                                    var distSq = Vector2.DistanceSquared(p1.Position, p2.Position);
                                    if (distSq < ConnectionDistanceSq)
                                    {
                                        float alpha = 1.0f - (float)Math.Sqrt(distSq) / ConnectionDistance;
                                        session.DrawLine(p1.Position, p2.Position,
                                            Color.FromArgb((byte)(alpha * 100), 0, 255, 255), 1);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 绘制粒子
            foreach (var p in _particles)
            {
                session.FillCircle(p.Position, 2, Colors.Cyan);
            }
        }

        private void BackgroundCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint((UIElement)sender);
            _mousePosition = new Vector2((float)ptr.Position.X, (float)ptr.Position.Y);
        }
    }
}