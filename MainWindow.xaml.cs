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
using Microsoft.UI.Xaml.Media;
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

        private const float ConnectionDistance = 150f;
        private const float ConnectionDistanceSq = ConnectionDistance * ConnectionDistance;

        private Dictionary<long, List<Particle>> _grid = new Dictionary<long, List<Particle>>();
        private Stack<List<Particle>> _listPool = new Stack<List<Particle>>();
        private int _gridCellSize = (int)ConnectionDistance;

        // [优化] 预计算透明度颜色表 (0-100)
        private Color[] _alphaColors;

        public MainWindow()
        {
            this.InitializeComponent();
            LoadSettingsFromDisk();

            // [优化] 初始化颜色查找表
            _alphaColors = new Color[101];
            for (int i = 0; i <= 100; i++)
            {
                // 这里的 i 对应之前的 (byte)(alpha * 100)
                // 颜色保持为 Cyan (0, 255, 255)
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
        private void LoadTools()
        {
            RegisterTool(new HomePage());
            RegisterTool(new MediaManagerPage());
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

            foreach (var list in _grid.Values)
            {
                list.Clear();
                _listPool.Push(list);
            }
            _grid.Clear();

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

        private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!IsParticleEffectEnabled) return;

            var session = args.DrawingSession;

            foreach (var kvp in _grid)
            {
                long key = kvp.Key;
                int cellX = (int)(key >> 32);
                int cellY = (int)(key & 0xFFFFFFFF);
                var cellParticles = kvp.Value;

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
                                        // [优化] 直接使用查找表，避免构造 struct
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