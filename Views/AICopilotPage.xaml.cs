using System;
using BlueSapphire.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using BlueSapphire.Helpers;
using Markdig;
using BlueSapphire.Models;
using Microsoft.UI.Xaml.Navigation;

namespace BlueSapphire.Views
{
    public sealed partial class AICopilotPage : Page
    {
        private readonly DeepSeekAIService _aiService;
        private readonly AIToolsRegistry _toolsRegistry;
        private readonly AIChatHistoryService _historyService;
        private readonly AITaskCenterService _taskCenter;
        private readonly AIMemoryService _memoryService;
        private readonly AIOfflineIntentService _offlineIntentService;
        private readonly AIInsightService _insightService;
        public ObservableCollection<ChatBubble> Messages { get; set; } = new();

        private readonly List<ChatMessage> _messageHistory = new();
        private readonly Task _initializationTask;
        private readonly Task _connectionCheckTask;
        private CancellationTokenSource? _responseCts;
        private bool _isProcessing;
        private bool _isConnected;

        public AICopilotPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            this.InitializeComponent();
            _aiService = App.Current.Services.GetRequiredService<DeepSeekAIService>();
            _toolsRegistry = App.Current.Services.GetRequiredService<AIToolsRegistry>();
            _historyService = App.Current.Services.GetRequiredService<AIChatHistoryService>();
            _taskCenter = App.Current.Services.GetRequiredService<AITaskCenterService>();
            _memoryService = App.Current.Services.GetRequiredService<AIMemoryService>();
            _offlineIntentService = App.Current.Services.GetRequiredService<AIOfflineIntentService>();
            _insightService = App.Current.Services.GetRequiredService<AIInsightService>();
            ChatList.ItemsSource = Messages;
            if (AppSettings.Get("ReduceMotion", false))
            {
                ChatList.ItemContainerTransitions = null;
            }
            _initializationTask = InitializeSystemPromptAsync(loadSavedHistory: true);
            _connectionCheckTask = CheckConnectionStatusAsync();
        }

        private async Task CheckConnectionStatusAsync()
        {
            bool isConnected = await _aiService.TestConnectionAsync();
            if (isConnected)
            {
                _isConnected = true;
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
                string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel)) ?? defaultModel;
                if (string.IsNullOrWhiteSpace(modelName)) modelName = defaultModel;
                
                string providerName = provider == "SiliconFlow" ? "硅基流动" : "官方直连";
                ConnectionStatusIndicator.Fill = Application.Current.Resources["AccentSafe"] as Brush;
                ConnectionStatusText.Text = $"{modelName} · {providerName}";
                ToolTipService.SetToolTip(ConnectionStatusText, $"当前模型：{modelName}（{providerName}）");
            }
            else
            {
                _isConnected = false;
                ConnectionStatusIndicator.Fill = Application.Current.Resources["TextMuted"] as Brush;
                ConnectionStatusText.Text = "未连接 · 前往设置";
                ToolTipService.SetToolTip(ConnectionStatusText, "请前往设置检查 API 密钥或刷新模型列表");
            }
        }

        private async Task InitializeSystemPromptAsync(
            bool loadSavedHistory,
            bool showWelcomeWhenEmpty = true)
        {
            var featureEnum = new System.Collections.Generic.List<string>();
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                foreach (var tool in mainWindow.Tools)
                {
                    featureEnum.Add(tool.Id);
                }
            }
            
            var prompt = await _toolsRegistry.GetSystemPromptAsync(featureEnum);
            lock (_messageHistory)
            {
                _messageHistory.RemoveAll(message =>
                    string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase));
                _messageHistory.Insert(0, prompt);
            }

            IReadOnlyList<ChatMessage> savedHistory = loadSavedHistory
                ? await _historyService.LoadAsync()
                : Array.Empty<ChatMessage>();
            if (savedHistory.Count > 0)
            {
                lock (_messageHistory)
                {
                    _messageHistory.AddRange(savedHistory);
                }

                foreach (ChatMessage message in savedHistory)
                {
                    if (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        AddMessage("用户", message.Content ?? string.Empty);
                    }
                    else if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(message.Content))
                    {
                        AddMessage("助理", message.Content);
                    }
                }
            }
            else if (showWelcomeWhenEmpty)
            {
                AddMessage("助理", "你好！我是蓝宝石智能助理。你可以让我分析清理结果、打开工具，或在确认后执行操作。");
            }
        }

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SendBtn_Click(this, new RoutedEventArgs());
            }
        }

        private void InputBox_GotFocus(object sender, RoutedEventArgs e)
        {
            InputBox.BorderBrush = Application.Current.Resources["AccentInspect"] as Brush;
        }

        private void InputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            InputBox.BorderBrush = Application.Current.Resources["BorderColor"] as Brush;
        }

        private async void ClearChatBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            await _initializationTask;
            await _connectionCheckTask;
            Messages.Clear();
            lock (_messageHistory)
            {
                _messageHistory.Clear();
            }
            await _historyService.ClearAsync();
            await InitializeSystemPromptAsync(loadSavedHistory: false);
        }

        private async void ExportChatBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("文本文件", new System.Collections.Generic.List<string> { ".txt" });
                picker.SuggestedFileName = $"AI对话记录_{DateTime.Now:yyyyMMdd_HHmmss}";

                WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var msg in Messages)
                    {
                        string role = msg.IsUser ? "我" : (msg.IsTool ? "技能执行" : "蓝宝石引擎");
                        sb.AppendLine($"[{role}]");
                        sb.AppendLine(msg.Content);
                        sb.AppendLine();
                    }
                    await Windows.Storage.FileIO.WriteTextAsync(file, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                // 导出是用户主动操作，失败必须可见，否则用户会误以为已成功导出。
                var dialog = new ContentDialog
                {
                    Title = "导出失败",
                    Content = $"对话记录未能写入所选文件：{ex.Message}",
                    CloseButtonText = "知道了",
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            await _initializationTask;
            var text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            InputBox.Text = string.Empty;
            SetProcessingState(true);
            _responseCts?.Dispose();
            _responseCts = new CancellationTokenSource();

            try
            {
                AddMessage("用户", text);
                lock (_messageHistory)
                {
                    _messageHistory.Add(new ChatMessage { Role = "user", Content = text });
                }

                // Show typing...
                var typingBubble = new ChatBubble { Content = "思考中...", IsUser = false };
                Messages.Add(typingBubble);
                _ = ScrollToBottomAsync();

                if (_isConnected)
                {
                    await ProcessMessageAsync(_responseCts.Token);
                }
                else
                {
                    (bool handled, string response) = await _offlineIntentService.TryHandleAsync(text);
                    Messages.Remove(typingBubble);
                    AddMessage(
                        handled ? "助理" : "系统",
                        handled
                            ? response
                            : $"{response}\n请在设置中检查 API Key 和网络连接。");
                    lock (_messageHistory)
                    {
                        _messageHistory.Add(new ChatMessage
                        {
                            Role = "assistant",
                            Content = response
                        });
                    }
                }

                var tb = Messages.FirstOrDefault(m => m.Content == "思考中...");
                if (tb != null) Messages.Remove(tb);
            }
            catch (OperationCanceledException)
            {
                var typing = Messages.FirstOrDefault(message => message.Content == "思考中...");
                if (typing != null) Messages.Remove(typing);
                AddMessage("系统", "已停止本次生成。");
            }
            catch (Exception ex)
            {
                var typing = Messages.FirstOrDefault(message => message.Content == "思考中...");
                if (typing != null) Messages.Remove(typing);
                AddMessage("系统", $"请求失败：{ex.Message}");
            }
            finally
            {
                List<ChatMessage> snapshot;
                lock (_messageHistory)
                {
                    snapshot = _messageHistory.ToList();
                }
                try
                {
                    await _historyService.SaveAsync(snapshot);
                }
                catch
                {
                    AddMessage("系统", "本次对话已完成，但本地历史记录保存失败。");
                }
                _responseCts?.Dispose();
                _responseCts = null;
                SetProcessingState(false);
                InputBox.Focus(FocusState.Programmatic);
            }
        }

        private async void TaskCenterBtn_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<AITaskRecord> tasks = _taskCenter.GetSnapshot();
            var taskPanel = new StackPanel { Spacing = 8 };

            if (tasks.Count == 0)
            {
                taskPanel.Children.Add(new TextBlock
                {
                    Text = "还没有后台任务。",
                    Foreground = Application.Current.Resources["TextMuted"] as Brush
                });
            }
            else
            {
                foreach (AITaskRecord task in tasks.Take(30))
                {
                    string recentTimeline = string.Join(
                        Environment.NewLine,
                        task.Timeline.TakeLast(3).Select(entry =>
                            $"{entry.Timestamp.ToLocalTime():HH:mm:ss} · {entry.Title} · {entry.Detail}"));
                    var title = new TextBlock
                    {
                        Text = $"{task.Title} · {task.StatusText} · {task.ProgressText}",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    };
                    var detail = new TextBlock
                    {
                        Text = $"{task.Summary}\n{recentTimeline}\n更新于 {task.UpdatedAtText}",
                        Foreground = Application.Current.Resources["TextMuted"] as Brush,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    };
                    var content = new StackPanel { Spacing = 4 };
                    content.Children.Add(title);
                    content.Children.Add(detail);

                    if (task.IsActive && task.CanCancel)
                    {
                        var cancelButton = new Button
                        {
                            Content = "取消任务",
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Tag = task.Id
                        };
                        cancelButton.Click += (_, _) =>
                        {
                            if (cancelButton.Tag is string taskId && _taskCenter.Cancel(taskId))
                            {
                                cancelButton.IsEnabled = false;
                                cancelButton.Content = "已发送取消请求";
                            }
                        };
                        content.Children.Add(cancelButton);
                    }

                    taskPanel.Children.Add(new Border
                    {
                        Background = Application.Current.Resources["PanelSurfaceStrong"] as Brush,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 9, 12, 9),
                        Child = content
                    });
                }
            }

            var dialog = new ContentDialog
            {
                Title = "任务中心",
                Content = new ScrollViewer
                {
                    Content = taskPanel,
                    MaxHeight = 520,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                PrimaryButtonText = "清除已结束任务",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _taskCenter.RemoveCompleted();
            }
        }

        private async void SuggestionsBtn_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<string> suggestions =
                await _insightService.BuildNonIntrusiveSuggestionsAsync();
            var panel = new StackPanel { Spacing = 8 };
            foreach (string suggestion in suggestions)
            {
                panel.Children.Add(new Border
                {
                    Background = Application.Current.Resources["PanelSurfaceStrong"] as Brush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 9, 12, 9),
                    Child = new TextBlock
                    {
                        Text = suggestion,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }

            var dialog = new ContentDialog
            {
                Title = "本地智能建议",
                Content = panel,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void MemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<AIMemoryEntry> entries = await _memoryService.GetEntriesAsync();
            bool wasPaused = await _memoryService.IsPausedAsync();

            var pauseSwitch = new ToggleSwitch
            {
                Header = "暂停长期记忆",
                IsOn = wasPaused,
                OffContent = "已启用",
                OnContent = "已暂停"
            };
            var list = new ListView
            {
                ItemsSource = entries,
                DisplayMemberPath = nameof(AIMemoryEntry.Content),
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 220
            };
            var editor = new TextBox
            {
                Header = "记忆内容",
                PlaceholderText = "选择一条记忆进行编辑，或直接输入以新增",
                TextWrapping = TextWrapping.Wrap,
                MaxLength = 500
            };
            var scope = new ComboBox
            {
                Header = "适用范围",
                ItemsSource = Enum.GetValues<AIMemoryScope>(),
                SelectedItem = AIMemoryScope.Global
            };
            var expiry = new CalendarDatePicker
            {
                Header = "有效期（留空表示长期有效）",
                PlaceholderText = "长期有效"
            };
            var enabledSwitch = new ToggleSwitch
            {
                Header = "启用这条记忆",
                IsOn = true
            };
            var selectedInfo = new TextBlock
            {
                Text = "未选择现有记忆：保存时将新增。",
                FontSize = 12,
                Foreground = Application.Current.Resources["TextMuted"] as Brush,
                TextWrapping = TextWrapping.Wrap
            };

            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not AIMemoryEntry selected)
                {
                    return;
                }
                editor.Text = selected.Content;
                scope.SelectedItem = selected.Scope;
                expiry.Date = selected.ExpiresAt;
                enabledSwitch.IsOn = selected.IsEnabled;
                selectedInfo.Text = $"{selected.ScopeText} · {selected.StatusText} · {selected.ExpiryText}";
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(pauseSwitch);
            panel.Children.Add(new TextBlock { Text = "已保存的记忆", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(list);
            panel.Children.Add(selectedInfo);
            panel.Children.Add(editor);
            panel.Children.Add(scope);
            panel.Children.Add(expiry);
            panel.Children.Add(enabledSwitch);

            var dialog = new ContentDialog
            {
                Title = "长期记忆管理",
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 560,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                PrimaryButtonText = "保存",
                SecondaryButtonText = "删除选中",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            await _memoryService.SetPausedAsync(pauseSwitch.IsOn);

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(editor.Text))
            {
                if (list.SelectedItem is AIMemoryEntry selected)
                {
                    await _memoryService.UpdateEntryAsync(
                        selected.Id,
                        editor.Text,
                        scope.SelectedItem is AIMemoryScope selectedScope ? selectedScope : AIMemoryScope.Global,
                        expiry.Date,
                        enabledSwitch.IsOn);
                }
                else
                {
                    await _memoryService.AddMemoryEntryAsync(
                        editor.Text,
                        scope.SelectedItem is AIMemoryScope selectedScope ? selectedScope : AIMemoryScope.Global,
                        expiry.Date,
                        "记忆管理页");
                }
                await InitializeSystemPromptAsync(loadSavedHistory: false, showWelcomeWhenEmpty: false);
            }
            else if (result == ContentDialogResult.Secondary &&
                     list.SelectedItem is AIMemoryEntry selected)
            {
                await _memoryService.RemoveEntryAsync(selected.Id);
                await InitializeSystemPromptAsync(loadSavedHistory: false, showWelcomeWhenEmpty: false);
            }
        }

        private async Task ProcessMessageAsync(CancellationToken cancellationToken)
        {
            var featureEnum = new System.Collections.Generic.List<string>();
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                foreach (var tool in mainWindow.Tools)
                {
                    featureEnum.Add(tool.Id);
                }
            }

            await _toolsRegistry.RunAgentLoopAsync(
                _messageHistory, 
                featureEnum, 
                (role, content, isAppend) => 
                {
                    DispatcherQueue.TryEnqueue(() => 
                    {
                        if (role == "assistant")
                        {
                            if (isAppend && Messages.Count > 0)
                            {
                                Messages.Last().Content += content;
                                _ = ScrollToBottomAsync();
                            }
                            else
                            {
                                var last = Messages.LastOrDefault();
                                if (last != null && last.Content == "思考中...")
                                {
                                    last.Content = content;
                                    _ = ScrollToBottomAsync();
                                }
                                else
                                {
                                    AddMessage("助理", content);
                                }
                            }
                        }
                        else if (role == "tool_progress")
                        {
                            AddMessage("系统", content, isInProgress: true);
                        }
                        else if (role == "tool_result")
                        {
                            ChatBubble? progressBubble = Messages.LastOrDefault(message => message.IsTool && message.IsInProgress);
                            if (progressBubble != null)
                            {
                                progressBubble.Content = content;
                                progressBubble.IsInProgress = false;
                            }
                        }
                    });
                },
                ShowConfirmationDialogAsync,
                cancellationToken);
        }

        private async Task<bool> ShowConfirmationDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "操作确认",
                Content = message,
                PrimaryButtonText = "允许",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _responseCts?.Cancel();
        }

        private void SetProcessingState(bool isProcessing)
        {
            _isProcessing = isProcessing;
            InputBox.IsEnabled = !isProcessing;
            SendBtn.Visibility = isProcessing ? Visibility.Collapsed : Visibility.Visible;
            StopBtn.Visibility = isProcessing ? Visibility.Visible : Visibility.Collapsed;
            ClearChatBtn.IsEnabled = !isProcessing;
            ExportChatBtn.IsEnabled = !isProcessing;
        }

        private void AddMessage(string role, string content, bool isInProgress = false)
        {
            bool isUser = role == "用户";
            bool isTool = role == "系统";
            Messages.Add(new ChatBubble
            {
                Content = content,
                IsUser = isUser,
                IsTool = isTool,
                IsInProgress = isInProgress
            });
            _ = ScrollToBottomAsync();
        }

        private async Task ScrollToBottomAsync()
        {
            await Task.Delay(50);
            ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
        }
    }

    public class ChatBubble : System.ComponentModel.INotifyPropertyChanged
    {
        private string _content = "";
        public string Content 
        { 
            get => _content; 
            set 
            {
                if (_content != value)
                {
                    _content = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Content)));
                }
            } 
        }

        public bool IsUser { get; set; }
        public bool IsTool { get; set; }
        private bool _isInProgress;
        public bool IsInProgress
        {
            get => _isInProgress;
            set
            {
                if (_isInProgress == value) return;
                _isInProgress = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsInProgress)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ToolProgressVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ToolCompletedVisibility)));
            }
        }

        public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public Visibility UserVisibility => IsUser ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AIVisibility => (!IsUser && !IsTool) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ToolVisibility => IsTool ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ToolProgressVisibility => IsTool && IsInProgress ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ToolCompletedVisibility => IsTool && !IsInProgress ? Visibility.Visible : Visibility.Collapsed;
        
        public string AvatarGlyph => IsUser ? "\xE77B" : (IsTool ? "\xE943" : "\xE9D9"); // E77B Contact, E943 ActionCenter/Tool, E9D9 Bot
        public string HeaderText => IsUser ? "我" : (IsTool ? "技能执行" : "蓝宝石引擎");
        
        public Brush BubbleBackground => IsUser
            ? TryResource("AccentInspectBg")
            : (IsTool ? TryResource("PanelSurface") : TryResource("PanelSurfaceStrong"));

        public Brush BubbleBorder => IsUser
            ? TryResource("AccentInspect")
            : TryResource("BorderColor");

        private static Brush TryResource(string key)
            => Application.Current.Resources.TryGetValue(key, out var b) ? (Brush)b : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}

