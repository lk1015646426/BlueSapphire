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
using BlueSapphire.Helpers;

namespace BlueSapphire.Views
{
    public sealed partial class AICopilotPage : Page
    {
        private readonly DeepSeekAIService _aiService;
        private readonly AIToolsRegistry _toolsRegistry;
        public ObservableCollection<ChatBubble> Messages { get; set; } = new();

        private System.Collections.Generic.List<ChatMessage> _messageHistory = new();

        public AICopilotPage()
        {
            this.InitializeComponent();
            _aiService = App.Current.Services.GetRequiredService<DeepSeekAIService>();
            _toolsRegistry = App.Current.Services.GetRequiredService<AIToolsRegistry>();
            ChatList.ItemsSource = Messages;
            
            _ = InitializeSystemPromptAsync();
            _ = CheckConnectionStatusAsync();
        }

        private async Task CheckConnectionStatusAsync()
        {
            bool isConnected = await _aiService.TestConnectionAsync();
            if (isConnected)
            {
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official");
                string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
                string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel));
                if (string.IsNullOrWhiteSpace(modelName)) modelName = defaultModel;
                
                string providerName = provider == "SiliconFlow" ? "硅基流动" : "官方直连";
                ConnectionStatusIndicator.Fill = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                ConnectionStatusText.Text = $"已连接至 {providerName} - {modelName}";
            }
            else
            {
                ConnectionStatusIndicator.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                ConnectionStatusText.Text = "未连接 (请前往设置检查 API Key 或刷新模型列表)";
            }
        }

        private async Task InitializeSystemPromptAsync()
        {
            var featureEnum = new System.Collections.Generic.List<string>();
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                foreach (var tool in mainWindow.Tools)
                {
                    featureEnum.Add(tool.Id);
                }
            }
            
            _messageHistory.Add(await _toolsRegistry.GetSystemPromptAsync(featureEnum));

            AddMessage("助理", "你好！我是蓝宝石智能引擎。我已全自动感知了系统中安装的工具并加载了您的长期记忆偏好。你可以直接告诉我你想做什么。");
        }

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SendBtn_Click(this, new RoutedEventArgs());
            }
        }

        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            InputBox.Text = string.Empty;
            InputBox.IsEnabled = false;
            SendBtn.IsEnabled = false;

            AddMessage("用户", text);
            _messageHistory.Add(new ChatMessage { Role = "user", Content = text });

            // Show typing...
            var typingBubble = new ChatBubble { Content = "思考中...", IsUser = false };
            Messages.Add(typingBubble);
            _ = ScrollToBottomAsync();

            await ProcessMessageAsync();

            var tb = Messages.FirstOrDefault(m => m.Content == "思考中...");
            if (tb != null) Messages.Remove(tb);

            InputBox.IsEnabled = true;
            SendBtn.IsEnabled = true;
            InputBox.Focus(FocusState.Programmatic);
        }

        private async Task ProcessMessageAsync()
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
                            AddMessage("系统", content);
                        }
                    });
                },
                ShowConfirmationDialogAsync);
        }

        private async Task<bool> ShowConfirmationDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "清理确认",
                Content = message,
                PrimaryButtonText = "确认清理",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void AddMessage(string role, string content)
        {
            bool isUser = role == "用户";
            Messages.Add(new ChatBubble { Content = content, IsUser = isUser });
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

        public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public SolidColorBrush BackgroundBrush => IsUser 
            ? new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue) { Opacity = 0.8 } 
            : new SolidColorBrush(Microsoft.UI.Colors.DarkGray) { Opacity = 0.4 };

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}

