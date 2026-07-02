using BlueSapphire.Helpers;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using BlueSapphire.Services;
using System.Collections.ObjectModel;

namespace BlueSapphire
{
    public sealed partial class SettingsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private string _appDisplayVersion = "版本信息读取失败";
        public string AppDisplayVersion
        {
            get => _appDisplayVersion;
            private set
            {
                _appDisplayVersion = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AppDisplayVersion)));
            }
        }

        private string _appBuildDate = "构建日期读取失败";
        public string AppBuildDate
        {
            get => _appBuildDate;
            private set
            {
                _appBuildDate = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AppBuildDate)));
            }
        }

        private int _versionTapCount;
        private readonly DispatcherTimer _clickResetTimer;

        public SettingsPage()
        {
            InitializeComponent();
            LoadVersionInfo();
            InitializeSettingsSafe();
            Bindings.Update();

            _clickResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _clickResetTimer.Tick += (_, _) =>
            {
                _versionTapCount = 0;
                _clickResetTimer.Stop();
            };

            var skillManager = App.Current.Services.GetRequiredService<WebSkillManager>();
            SkillsList.ItemsSource = skillManager.Skills;

            var mcpManager = App.Current.Services.GetRequiredService<McpServerManager>();
            mcpManager.OnServersChanged += RefreshMcpServersList;
            RefreshMcpServersList();
            
            // 尝试启动所有已启用的 MCP 服务器
            _ = mcpManager.StartAllEnabledServersAsync();
        }

        private async void LoadVersionInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                
                try
                {
                    var logService = App.Current.Services.GetRequiredService<DevLogDataService>();
                    var logs = await logService.LoadLogsAsync();
                    var latestLog = logs.OrderByDescending(l => l.Timestamp).FirstOrDefault();
                    if (latestLog != null)
                    {
                        AppDisplayVersion = $"版本 {latestLog.Version}";
                    }
                    else
                    {
                        var version = assembly.GetName().Version;
                        if (version != null)
                            AppDisplayVersion = $"版本 {version.Major}.{version.Minor}.{version.Build}";
                    }
                }
                catch
                {
                    var version = assembly.GetName().Version;
                    if (version != null)
                        AppDisplayVersion = $"版本 {version.Major}.{version.Minor}.{version.Build}";
                }

                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    AppBuildDate = File.GetLastWriteTime(assembly.Location).ToString("yyyy.MM.dd");
                }
            }
            catch
            {
                AppBuildDate = "读取异常";
            }
        }

        private void InitializeSettingsSafe()
        {
            ParticleSwitch.Toggled -= ParticleSwitch_Toggled;

            bool targetState = AppSettings.Get("IsParticleEffectEnabled", true);
            if (App.CurrentWindow is MainWindow window && AppSettings.Get<bool?>("IsParticleEffectEnabled", null) == null)
            {
                targetState = window.IsParticleEffectEnabled;
            }

            ParticleSwitch.IsOn = targetState;
            ParticleSwitch.Toggled += ParticleSwitch_Toggled;

            ApiProviderComboBox.SelectionChanged -= ApiProviderComboBox_SelectionChanged;
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            ApiProviderComboBox.SelectedIndex = provider == "SiliconFlow" ? 1 : 0;
            ApiProviderComboBox.SelectionChanged += ApiProviderComboBox_SelectionChanged;

            DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
            DeepSeekApiKeyBox.Password = AppSettings.GetSecret($"DeepSeekApiKey_{provider}") ?? AppSettings.GetSecret("DeepSeekApiKey") ?? string.Empty;
            DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;

            ApiModelComboBox.SelectionChanged -= ApiModelComboBox_SelectionChanged;
            string savedModel = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat")) ?? string.Empty;
            if (!string.IsNullOrEmpty(savedModel))
            {
                ApiModelComboBox.Items.Add(new ComboBoxItem { Content = savedModel, Tag = savedModel });
                ApiModelComboBox.SelectedIndex = 0;
            }
            ApiModelComboBox.SelectionChanged += ApiModelComboBox_SelectionChanged;
        }

        private void DeepSeekApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            AppSettings.SaveSecret($"DeepSeekApiKey_{provider}", DeepSeekApiKeyBox.Password);
            
            // 为了兼容性，也保存一份到默认的 key 中（如果之前没有特定提供商配置，方便回滚）
            AppSettings.SaveSecret("DeepSeekApiKey", DeepSeekApiKeyBox.Password);
        }

        private void ApiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                string oldProvider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                if (oldProvider != tag)
                {
                    AppSettings.Save("DeepSeekApiProvider", tag);
                    
                    DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
                    DeepSeekApiKeyBox.Password = AppSettings.GetSecret($"DeepSeekApiKey_{tag}") ?? AppSettings.GetSecret("DeepSeekApiKey") ?? string.Empty;
                    DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;

                    if (ApiModelComboBox != null)
                    {
                        ApiModelComboBox.Items.Clear();
                        ApiModelComboBox.PlaceholderText = "供应商已切换，请重新获取模型";
                    }
                }
            }
        }

        private void ApiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiModelComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                AppSettings.Save($"DeepSeekApiModel_{provider}", tag);
                AppSettings.Save("DeepSeekApiModel", tag); // For compatibility
            }
        }

        private async void RefreshModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            await FetchModelsAsync();
        }

        private async void ApiModelComboBox_DropDownOpened(object sender, object e)
        {
            if (ApiModelComboBox.Items.Count <= 1)
            {
                await FetchModelsAsync();
            }
        }

        private async Task FetchModelsAsync()
        {
            try
            {
                RefreshModelsBtn.IsEnabled = false;
                ApiModelComboBox.PlaceholderText = "正在获取模型...";
                
                string? currentSelection = (ApiModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                ApiModelComboBox.Items.Clear();

                // 强制保存最新的 API Key 到对应提供商
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                AppSettings.SaveSecret($"DeepSeekApiKey_{provider}", DeepSeekApiKeyBox.Password);

                var aiService = App.Current.Services.GetRequiredService<DeepSeekAIService>();
                var result = await aiService.GetAvailableModelsAsync();

                if (result.Models.Count > 0)
                {
                    string savedModel = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", "")) ?? string.Empty;
                    int selectedIndex = -1;
                    for (int i = 0; i < result.Models.Count; i++)
                    {
                        ApiModelComboBox.Items.Add(new ComboBoxItem { Content = result.Models[i], Tag = result.Models[i] });
                        if (result.Models[i] == savedModel || result.Models[i] == currentSelection) selectedIndex = i;
                    }
                    if (selectedIndex >= 0) ApiModelComboBox.SelectedIndex = selectedIndex;
                    else ApiModelComboBox.SelectedIndex = 0;
                    
                    ApiModelComboBox.PlaceholderText = "请选择连接模型";
                }
                else
                {
                    // 将真实的报错信息显示给用户
                    ApiModelComboBox.PlaceholderText = !string.IsNullOrEmpty(result.Error) 
                        ? result.Error 
                        : "获取失败，请检查 API Key";
                }
            }
            finally
            {
                RefreshModelsBtn.IsEnabled = true;
            }
        }

        private void ParticleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ParticleSwitch.IsOn;
            WeakReferenceMessenger.Default.Send(new ToggleParticleMessage(isEnabled));
            AppSettings.Save("IsParticleEffectEnabled", isEnabled);
        }

        private async void SecretVersion_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _versionTapCount++;
            _clickResetTimer.Stop();
            _clickResetTimer.Start();

            if (_versionTapCount < 5)
            {
                TriggerMicroBounce(VersionTextBlock);
                return;
            }

            _versionTapCount = 0;
            _clickResetTimer.Stop();

            await TriggerCyberPulseEffect(VersionTextBlock);
            Frame.Navigate(typeof(Views.DevLogPage));
        }

        private void TriggerMicroBounce(TextBlock target)
        {
            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var scaleXAnimation = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

            var scaleYAnimation = new DoubleAnimation
            {
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleYAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();
        }

        private async Task TriggerCyberPulseEffect(TextBlock target)
        {
            var originalBrush = target.Foreground;
            target.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);

            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            target.RenderTransform = scaleTransform;

            var storyboard = new Storyboard();
            var easeFunction = new SineEase { EasingMode = EasingMode.EaseInOut };

            var scaleXAnimation = new DoubleAnimation
            {
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleXAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");

            var scaleYAnimation = new DoubleAnimation
            {
                To = 1.15,
                Duration = TimeSpan.FromMilliseconds(120),
                AutoReverse = true,
                EasingFunction = easeFunction
            };
            Storyboard.SetTarget(scaleYAnimation, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");

            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();

            await Task.Delay(300);
            target.Foreground = originalBrush;
        }

        private void SecretVersion_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }

        private void SecretVersion_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }

        // --- MCP Server Management UI ---
        public ObservableCollection<McpServerUIModel> McpServers { get; } = new();

        private void RefreshMcpServersList()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var mcpManager = App.Current.Services.GetRequiredService<McpServerManager>();
                McpServers.Clear();
                foreach (var server in mcpManager.GetServers())
                {
                    bool isRunning = mcpManager.IsServerRunning(server.Id);
                    McpServers.Add(new McpServerUIModel
                    {
                        Id = server.Id,
                        Name = server.Name,
                        Command = $"{server.Command} {server.Arguments}",
                        StatusText = isRunning ? "● 运行中" : "○ 已停止",
                        StatusColor = isRunning ? new SolidColorBrush(Microsoft.UI.Colors.LightGreen) : new SolidColorBrush(Microsoft.UI.Colors.Gray)
                    });
                }
                McpServersList.ItemsSource = McpServers;
            });
        }

        private async void AddMcpBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "添加 MCP 服务器",
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var nameBox = new TextBox { PlaceholderText = "例如：Puppeteer", Margin = new Thickness(0, 0, 0, 10) };
            var cmdBox = new TextBox { PlaceholderText = "命令，例如：npx", Margin = new Thickness(0, 0, 0, 10) };
            var argsBox = new TextBox { PlaceholderText = "参数，例如：-y @modelcontextprotocol/server-puppeteer", Margin = new Thickness(0, 0, 0, 10) };
            var envBox = new TextBox 
            { 
                PlaceholderText = "格式: KEY=VALUE (换行分隔，如 GITHUB_TOKEN=abc)", 
                AcceptsReturn = true, 
                TextWrapping = TextWrapping.Wrap,
                Height = 80,
                Margin = new Thickness(0, 0, 0, 10) 
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "服务器名称", Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock { Text = "执行命令", Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(cmdBox);
            panel.Children.Add(new TextBlock { Text = "执行参数", Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(argsBox);
            panel.Children.Add(new TextBlock { Text = "环境变量 (可选)", Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(envBox);

            dialog.Content = panel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text) && !string.IsNullOrWhiteSpace(cmdBox.Text))
            {
                var envDict = new System.Collections.Generic.Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(envBox.Text))
                {
                    var lines = envBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            envDict[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }

                var config = new McpServerConfig
                {
                    Name = nameBox.Text,
                    Command = cmdBox.Text,
                    Arguments = argsBox.Text,
                    EnvironmentVariables = envDict,
                    IsEnabled = true
                };

                var mcpManager = App.Current.Services.GetRequiredService<McpServerManager>();
                mcpManager.AddOrUpdateServer(config);
                await mcpManager.StartServerAsync(config.Id);
            }
        }

        private void DeleteMcpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var mcpManager = App.Current.Services.GetRequiredService<McpServerManager>();
                mcpManager.RemoveServer(id);
            }
        }

        private async void AddSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "添加远程 OpenAPI 技能",
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var urlBox = new TextBox { PlaceholderText = "例如：https://api.example.com/openapi.json", Margin = new Thickness(0, 0, 0, 10) };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "技能在线规范地址 (URL)", Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(urlBox);
            
            dialog.Content = panel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(urlBox.Text))
            {
                // 获取用户输入的URL并展示代理选项弹窗
                var proxyDialog = new Views.SkillProxyConfigDialog("新技能")
                {
                    XamlRoot = this.XamlRoot
                };

                var proxyResult = await proxyDialog.ShowAsync();
                
                if (!proxyDialog.Result.HasValue)
                {
                    // 用户点击了取消按钮或关闭了对话框，中止添加过程
                    return;
                }

                bool useDomestic = proxyDialog.Result.Value;

                var skillManager = App.Current.Services.GetRequiredService<WebSkillManager>();
                
                // 将用户的代理选择传给添加逻辑，以便首次加载就使用正确的网络配置
                var (addedSkill, errorMsg) = await skillManager.AddSkillAsync(urlBox.Text, useDomestic);
                
                if (addedSkill == null)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "技能添加失败",
                        Content = $"无法加载该技能，请检查网址是否正确，或尝试切换网络选项。\n\n错误原因: {errorMsg}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
                else
                {
                    // 若需要更新名称等
                    addedSkill.UseDomesticNetwork = useDomestic;
                    skillManager.SaveConfig();
                }
            }
        }

        private void DeleteSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var skillManager = App.Current.Services.GetRequiredService<WebSkillManager>();
                skillManager.RemoveSkillAsync(id);
            }
        }
    }

    public class McpServerUIModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string StatusText { get; set; } = "";
        public Brush StatusColor { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.White);
    }
}

