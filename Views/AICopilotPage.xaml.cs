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

namespace BlueSapphire.Views
{
    public sealed partial class AICopilotPage : Page
    {
        private readonly DeepSeekAIService _aiService;
        private readonly AIToolsRegistry _toolsRegistry;
        private readonly AIChatHistoryService _historyService;
        public ObservableCollection<ChatBubble> Messages { get; set; } = new();

        private readonly List<ChatMessage> _messageHistory = new();
        private readonly Task _initializationTask;
        private CancellationTokenSource? _responseCts;
        private bool _isProcessing;

        public AICopilotPage()
        {
            this.InitializeComponent();
            _aiService = App.Current.Services.GetRequiredService<DeepSeekAIService>();
            _toolsRegistry = App.Current.Services.GetRequiredService<AIToolsRegistry>();
            _historyService = App.Current.Services.GetRequiredService<AIChatHistoryService>();
            ChatList.ItemsSource = Messages;
            if (AppSettings.Get("ReduceMotion", false))
            {
                ChatList.ItemContainerTransitions = null;
            }
            Unloaded += AICopilotPage_Unloaded;
            
            _initializationTask = InitializeSystemPromptAsync(loadSavedHistory: true);
            _ = CheckConnectionStatusAsync();
        }

        private void AICopilotPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _responseCts?.Cancel();
            Unloaded -= AICopilotPage_Unloaded;
        }

        private async Task CheckConnectionStatusAsync()
        {
            bool isConnected = await _aiService.TestConnectionAsync();
            if (isConnected)
            {
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
                string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel)) ?? defaultModel;
                if (string.IsNullOrWhiteSpace(modelName)) modelName = defaultModel;
                
                string providerName = provider == "SiliconFlow" ? "硅基流动" : "官方直连";
                ConnectionStatusIndicator.Fill = Application.Current.Resources["AccentSafe"] as Brush;
                ConnectionStatusText.Text = $"已连接至 {providerName} - {modelName}";
            }
            else
            {
                ConnectionStatusIndicator.Fill = Application.Current.Resources["TextMuted"] as Brush;
                ConnectionStatusText.Text = "未连接 (请前往设置检查 API Key 或刷新模型列表)";
            }
        }

        private async Task InitializeSystemPromptAsync(bool loadSavedHistory)
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
            else
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
            catch
            {
                // 忽略导出异常
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

                await ProcessMessageAsync(_responseCts.Token);

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

