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
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Windows.UI;

namespace BlueSapphire
{
    public sealed partial class MainWindow : Window
    {
        public bool IsParticleEffectEnabled { get; private set; } = true;

        private List<ITool> _tools = new List<ITool>();
        private List<Particle> _particles = new List<Particle>();
        private Random _random = new Random();
        private Vector2 _mousePosition = new Vector2(-1000, -1000);

        private const float ConnectionDistance = 150f;
        private const float ConnectionDistanceSq = ConnectionDistance * ConnectionDistance;

        // [极客优化] 抛弃 Dictionary，预分配一块支持高达 6000x4500 超高分辨率的二维数组
        // 最大支持 40列 x 30行 的网格划分，彻底转化为 O(1) 内存偏移量寻址
        private const int MaxGridCols = 40;
        private const int MaxGridRows = 30;
        private List<Particle>[,] _gridArray;
        private int _currentCols = 0;
        private int _currentRows = 0;

        private int _gridCellSize = (int)ConnectionDistance;

        // [优化] 预计算透明度颜色表 (0-100)
        private Color[] _alphaColors;

        public MainWindow()
        {
            this.InitializeComponent();

            // 初始化二维数组网格对象池
            _gridArray = new List<Particle>[MaxGridCols, MaxGridRows];
            for (int i = 0; i < MaxGridCols; i++)
            {
                for (int j = 0; j < MaxGridRows; j++)
                {
                    _gridArray[i, j] = new List<Particle>(16);
                }
            }

            LoadSettingsFromDisk();

            // [优化] 初始化颜色查找表
            _alphaColors = new Color[101];
            for (int i = 0; i <= 100; i++)
            {
                _alphaColors[i] = Color.FromArgb((byte)i, 0, 255, 255);
            }

            if (AppTitleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);
            }
            CustomizeTitleBar();
            LoadTools();

            WeakReferenceMessenger.Default.Register<ToggleParticleMessage>(this, (r, m) =>
            {
                IsParticleEffectEnabled = m.Value;
                if (IsParticleEffectEnabled) BackgroundCanvas?.Invalidate();
            });

            if (NavView.MenuItems.Count > 0) NavView.SelectedItem = NavView.MenuItems[0];
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, object e)
        {
            if (!IsParticleEffectEnabled || BackgroundCanvas == null) return;
            UpdateLogic();
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

        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(HomePage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(MediaManagerPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.DevLogPage))]
        private void LoadTools()
        {
            RegisterTool(App.Current.Services.GetRequiredService<HomeTool>());
            RegisterTool(App.Current.Services.GetRequiredService<MediaManagerTool>());
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

        private void UpdateLogic()
        {
            if (BackgroundCanvas == null) return;
            if (BackgroundCanvas.ActualWidth <= 0 || BackgroundCanvas.ActualHeight <= 0) return;

            float width = (float)BackgroundCanvas.ActualWidth;
            float height = (float)BackgroundCanvas.ActualHeight;

            // 动态计算当前需要的网格范围（向上取整并限制在最大预分配范围内，防越界）
            _currentCols = Math.Min((int)(width / _gridCellSize) + 1, MaxGridCols);
            _currentRows = Math.Min((int)(height / _gridCellSize) + 1, MaxGridRows);

            // 高速清空当前可视范围内的网格数据 (规避 Dictionary.Clear() 造成的哈希桶重置开销)
            for (int i = 0; i < _currentCols; i++)
            {
                for (int j = 0; j < _currentRows; j++)
                {
                    _gridArray[i, j].Clear();
                }
            }

            foreach (var p in _particles)
            {
                p.Update(width, height, _mousePosition);

                int cellX = (int)(p.Position.X / _gridCellSize);
                int cellY = (int)(p.Position.Y / _gridCellSize);

                // 边界安全检查，分发到二维数组
                if (cellX >= 0 && cellX < _currentCols && cellY >= 0 && cellY < _currentRows)
                {
                    _gridArray[cellX, cellY].Add(p);
                }
            }
        }

        private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!IsParticleEffectEnabled) return;

            var session = args.DrawingSession;

            // 内存连续寻址，遍历当前屏幕可见的二维网格
            for (int cellX = 0; cellX < _currentCols; cellX++)
            {
                for (int cellY = 0; cellY < _currentRows; cellY++)
                {
                    var cellParticles = _gridArray[cellX, cellY];
                    if (cellParticles.Count == 0) continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int neighborX = cellX + dx;
                            int neighborY = cellY + dy;

                            if (neighborX >= 0 && neighborX < _currentCols && neighborY >= 0 && neighborY < _currentRows)
                            {
                                var neighborParticles = _gridArray[neighborX, neighborY];
                                foreach (var p1 in cellParticles)
                                {
                                    foreach (var p2 in neighborParticles)
                                    {
                                        if (p1 == p2) continue;

                                        var distSq = Vector2.DistanceSquared(p1.Position, p2.Position);
                                        if (distSq < ConnectionDistanceSq)
                                        {
                                            float alpha = 1.0f - (float)Math.Sqrt(distSq) / ConnectionDistance;
                                            int index = (int)(alpha * 100);
                                            if (index < 0) index = 0;
                                            if (index > 100) index = 100;

                                            session.DrawLine(p1.Position, p2.Position, _alphaColors[index], 1);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

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