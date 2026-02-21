using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using BlueSapphire.ViewModels;
using BlueSapphire.Models;
using System;
using Windows.Storage;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogPage : Page
    {
        public DevLogViewModel ViewModel { get; } = new DevLogViewModel();

        public DevLogPage()
        {
            this.InitializeComponent();
            this.Loaded += DevLogPage_Loaded;
        }

        private async void DevLogPage_Loaded(object sender, RoutedEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(300);

            bool isFirstRun = true;
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.GetItemAsync("DevMatrixLog.json");
                if (item != null)
                {
                    isFirstRun = false;
                }
            }
            catch
            {
                isFirstRun = true;
            }

            if (ViewModel.Logs.Count == 0 && isFirstRun)
            {
                ViewModel.Logs.Add(new DevLogItem
                {
                    Title = "视觉重构与零 GC 渲染",
                    Description = "项目从功能原型迈向商业级产品的关键里程碑。引入了全新的指挥中心首页，重写了粒子渲染管线以实现零内存分配，并通过消息总线彻底解决了架构耦合问题。",
                    Version = "v0.6.0",
                    FullContent = "详细文档请参考 v0.6.0 发布说明。\n\n- 采用手动游戏循环，实现真正的 60FPS 零 GC 渲染。\n- 引入 Messenger 消息总线，彻底移除 Singleton 强耦合。\n- 全新的玻璃拟态 2.0 与动态光效反馈设计。"
                });
                ViewModel.Logs.Add(new DevLogItem
                {
                    Title = "媒体管家深度升级",
                    Description = "标志着应用从单纯的图片浏览工具升级为全功能的媒体管家。重点构建了文件管理能力（删除、智能去重）和沉浸式交互系统。",
                    Version = "v0.5.0",
                    FullContent = "详细文档请参考 v0.5.0 发布说明。\n\n- 全能排序系统：支持按名称、日期、大小及升降序排列。\n- 智能去重：采用 MD5 深度校验，精准定位并清除重复文件。\n- 界面分层架构：引入 StatusOverlay 遮罩，提升交互体验。"
                });
                ViewModel.Logs.Add(new DevLogItem
                {
                    Title = "性能优化与数据持久化",
                    Description = "本版本是项目的性能里程碑，重点解决了大数据量下的可用性问题，引入了虚拟化技术和数据持久化层，实现数千文件的零延迟响应。",
                    Version = "v0.4.0",
                    FullContent = "详细文档请参考 v0.4.0 发布说明。\n\n- 极速索引：利用系统索引直接获取文件元数据，文件夹瞬开。\n- 虚拟化无限滚动：实现 ISupportIncrementalLoading，大幅降低内存占用。\n- 路径记忆：使用 FutureAccessList 实现自动权限维持与状态恢复。"
                });
                ViewModel.Logs.Add(new DevLogItem
                {
                    Title = "核心架构与基础功能上线",
                    Description = "项目首次大版本发布。正式确立了“宿主 + 插件”的反射式动态加载架构，完成了系统仪表盘与基础图片管家模块的开发。",
                    Version = "v0.3.0",
                    FullContent = "详细文档请参考 v0.3.0 发布说明。\n\n- 核心架构：MainWindow 负责框架，具体页面作为插件动态加载。\n- 图形渲染：引入 Win2D 高性能粒子神经网络背景。\n- 基础功能：实现图片非阻塞异步加载，支持基础配置与性能模式。"
                });

                SaveViewModelData();
            }
        }

        private async void OpenInputDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DevLogInputDialog { XamlRoot = this.XamlRoot };
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.NodeTitle))
            {
                var newItem = new DevLogItem
                {
                    Title = dialog.NodeTitle,
                    Description = dialog.NodeDescription,
                    Version = string.IsNullOrWhiteSpace(dialog.NodeVersion) ? "未知版本" : dialog.NodeVersion,
                    FullContent = string.IsNullOrWhiteSpace(dialog.NodeFullContent) ? "暂无详细文档内容。" : dialog.NodeFullContent,
                    Status = DevLogStatus.Completed,
                    Timestamp = dialog.NodeDate ?? DateTime.Now
                };

                ViewModel.Logs.Insert(0, newItem);
                SaveViewModelData();
            }
        }

        private void DeleteLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is DevLogItem item)
            {
                // 转移焦点，防止带有焦点的按钮瞬间消失导致系统级死锁
                RootPage.IsTabStop = true;
                RootPage.Focus(FocusState.Programmatic);

                // 核心修复：直接从 ViewModel 的集合中暴力移除该项
                // 抛弃复杂的命令接口转换，绝对不会出现按钮变灰却删不掉的情况
                if (ViewModel.Logs.Contains(item))
                {
                    ViewModel.Logs.Remove(item);
                    SaveViewModelData(); // 同步触发本地 JSON 保存
                }
            }
        }

        private void SaveViewModelData()
        {
            try
            {
                var type = ViewModel.GetType();
                var saveMethod = type.GetMethod("SaveDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                saveMethod?.Invoke(ViewModel, null);
            }
            catch { }
        }

        private void OpenDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is DevLogItem item)
            {
                DetailTitle.Text = item.Title;
                DetailVersion.Text = $"版本号: {item.Version}  |  更新时间: {item.DisplayTime}";
                DetailContent.Text = string.IsNullOrWhiteSpace(item.FullContent) ? "暂无详细文档内容。" : item.FullContent;

                DetailOverlay.Visibility = Visibility.Visible;
            }
        }

        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            DetailOverlay.Visibility = Visibility.Collapsed;
        }

        private void OpenDetail_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }

        private void OpenDetail_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }
    }
}