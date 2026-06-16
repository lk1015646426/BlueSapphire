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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
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

        private const int MaxGridCols = 40;
        private const int MaxGridRows = 30;
        private List<Particle>[,] _gridArray;
        private int _currentCols = 0;
        private int _currentRows = 0;

        private int _gridCellSize = (int)ConnectionDistance;

        private Color[] _alphaColors;

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

                // [核心修复] 移除原来的 if 限制。
                // 无论开启还是关闭，状态切换时都必须强制重绘一次画布。
                // 如果是关闭，这最后一次重绘会让画布执行下方的 OnDraw 逻辑并清空画面，彻底消灭残留的粒子。
                BackgroundCanvas?.Invalidate();
            });

            WeakReferenceMessenger.Default.Register<ShowTipMessage>(this, async (r, m) =>
            {
                var dialog = new ContentDialog
                {
                    Title = m.Title,
                    Content = m.Message,
                    CloseButtonText = "确定",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
            });

            SelectInitialTool();
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
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CleanerAssistantPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.DevLogPage))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Views.AICopilotPage))]
        private void LoadTools()
        {
            RegisterTool(App.Current.Services.GetRequiredService<HomeTool>());
            RegisterTool(App.Current.Services.GetRequiredService<AICopilotTool>());
            RegisterTool(App.Current.Services.GetRequiredService<MediaManagerTool>());
            RegisterTool(App.Current.Services.GetRequiredService<CleanerAssistantTool>());
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
            if (args.IsSettingsSelected) ContentFrame.Navigate(typeof(SettingsPage));
            else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                var tool = _tools.FirstOrDefault(t => t.Id == tag);
                if (tool != null) ContentFrame.Navigate(tool.ContentPage);
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

        private void UpdateLogic()
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
                p.Update(width, height, _mousePosition);

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
                args.DrawingSession.Clear(Colors.Black);
                return;
            }

            var session = args.DrawingSession;

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
