using BlueSapphire.Helpers;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        private readonly DispatcherTimer _apiKeySaveTimer;
        private readonly McpServerManager _mcpManager;
        private bool _initializingAppearance;
        private int _versionTapCount;
        private DateTimeOffset _lastVersionTapTime = DateTimeOffset.MinValue;

        public SettingsPage()
        {
            _apiKeySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _apiKeySaveTimer.Tick += (_, _) =>
            {
                _apiKeySaveTimer.Stop();
                SaveCurrentApiKey();
            };

            InitializeComponent();
            LoadVersionInfo();
            InitializeSettingsSafe();
            Bindings.Update();

            var skillManager = App.Current.Services.GetRequiredService<WebSkillManager>();
            SkillsList.ItemsSource = skillManager.Skills;
            var agentSkillManager = App.Current.Services.GetRequiredService<AgentSkillManager>();
            AgentSkillsList.ItemsSource = agentSkillManager.Skills;

            _mcpManager = App.Current.Services.GetRequiredService<McpServerManager>();
            _mcpManager.OnServersChanged += RefreshMcpServersList;
            Unloaded += SettingsPage_Unloaded;
            RefreshMcpServersList();
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_apiKeySaveTimer.IsEnabled)
            {
                _apiKeySaveTimer.Stop();
                SaveCurrentApiKey();
            }
            _mcpManager.OnServersChanged -= RefreshMcpServersList;
            Unloaded -= SettingsPage_Unloaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyThemePresetGridLayout(SettingsLayoutRoot.ActualWidth);
            DispatcherQueue.TryEnqueue(() =>
                SettingsScrollViewer.ChangeView(null, 0, null, disableAnimation: true));
        }

        private void SettingsLayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyThemePresetGridLayout(e.NewSize.Width);
        }

        private void ApplyThemePresetGridLayout(double availableWidth)
        {
            int columns = availableWidth >= 900 ? 4 : availableWidth >= 560 ? 2 : 1;
            RadioButton[] presets =
            [
                PresetDefault,
                PresetAzure,
                PresetCobalt,
                PresetGraphite,
                PresetLagoon,
                PresetInk,
                PresetOchre,
                PresetSepia
            ];
            int rows = (int)Math.Ceiling(presets.Length / (double)columns);

            ThemePresetGrid.ColumnDefinitions.Clear();
            ThemePresetGrid.RowDefinitions.Clear();
            for (int column = 0; column < columns; column++)
            {
                ThemePresetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int row = 0; row < rows; row++)
            {
                ThemePresetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int index = 0; index < presets.Length; index++)
            {
                Grid.SetRow(presets[index], index / columns);
                Grid.SetColumn(presets[index], index % columns);
            }
        }

        private void LoadVersionInfo()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                
                string version = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                    .Split('+')[0]
                    ?? assembly.GetName().Version?.ToString(3)
                    ?? "未知";
                AppDisplayVersion = $"版本 {version}";

                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    AppBuildDate = $"文件日期 {File.GetLastWriteTime(assembly.Location):yyyy.MM.dd}";
                }
            }
            catch
            {
                AppBuildDate = "读取异常";
            }
        }

        private void InitializeSettingsSafe()
        {
            _initializingAppearance = true;
            string theme = AppSettings.Get("AppTheme", "System") ?? "System";
            ThemeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;
            ThemeComboBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            ThemeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;

            string preset = AppSettings.Get("ThemePreset", "default") ?? "default";
            RadioButton selectedPreset = preset switch
            {
                "sky" or "azure" => PresetAzure,
                "cobalt" => PresetCobalt,
                "graphite" => PresetGraphite,
                "lagoon" => PresetLagoon,
                "ink" => PresetInk,
                "ochre" => PresetOchre,
                "sepia" => PresetSepia,
                _ => PresetDefault
            };
            selectedPreset.IsChecked = true;

            string fontSize = AppSettings.Get("UiFontSize", "standard") ?? "standard";
            RadioButton selectedFontSize = fontSize switch
            {
                "small" => FontSmall,
                "medium" => FontMedium,
                "large" => FontLarge,
                _ => FontStandard
            };
            selectedFontSize.IsChecked = true;

            ReduceMotionSwitch.Toggled -= ReduceMotionSwitch_Toggled;
            ReduceMotionSwitch.IsOn = AppSettings.Get("ReduceMotion", false);
            ReduceMotionSwitch.Toggled += ReduceMotionSwitch_Toggled;

            ApiProviderComboBox.SelectionChanged -= ApiProviderComboBox_SelectionChanged;
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            ApiProviderComboBox.SelectedIndex = provider == "SiliconFlow" ? 1 : 0;
            ApiProviderComboBox.SelectionChanged += ApiProviderComboBox_SelectionChanged;

            DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
            DeepSeekApiKeyBox.Password = LoadProviderApiKey(provider);
            DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;

            ApiModelComboBox.SelectionChanged -= ApiModelComboBox_SelectionChanged;
            string savedModel = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat")) ?? string.Empty;
            if (!string.IsNullOrEmpty(savedModel))
            {
                ApiModelComboBox.Items.Add(new ComboBoxItem { Content = savedModel, Tag = savedModel });
                ApiModelComboBox.SelectedIndex = 0;
            }
            ApiModelComboBox.SelectionChanged += ApiModelComboBox_SelectionChanged;
            _initializingAppearance = false;
        }

        private void ThemePreset_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializingAppearance || sender is not RadioButton { Tag: string preset })
            {
                return;
            }

            App.ApplyThemePreset(preset);
        }

        private void UiFontSize_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializingAppearance || sender is not RadioButton { Tag: string size })
            {
                return;
            }

            App.ApplyUiFontSize(size);
        }

        private void DeepSeekApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _apiKeySaveTimer.Stop();
            _apiKeySaveTimer.Start();
        }

        private void SaveCurrentApiKey()
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            AppSettings.SaveSecret($"DeepSeekApiKey_{provider}", DeepSeekApiKeyBox.Password);
        }

        private static string LoadProviderApiKey(string provider)
        {
            string? providerKey = AppSettings.GetSecret($"DeepSeekApiKey_{provider}");
            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                return providerKey;
            }

            // 历史版本只有 DeepSeek 官方使用未分供应商的旧键。
            return string.Equals(provider, "Official", StringComparison.OrdinalIgnoreCase)
                ? AppSettings.GetSecret("DeepSeekApiKey") ?? string.Empty
                : string.Empty;
        }

        private void ApiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                string oldProvider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                if (oldProvider != tag)
                {
                    _apiKeySaveTimer.Stop();
                    AppSettings.SaveSecret($"DeepSeekApiKey_{oldProvider}", DeepSeekApiKeyBox.Password);
                    AppSettings.Save("DeepSeekApiProvider", tag);
                    
                    DeepSeekApiKeyBox.PasswordChanged -= DeepSeekApiKeyBox_PasswordChanged;
                    DeepSeekApiKeyBox.Password = LoadProviderApiKey(tag);
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

                // 在发起连接测试前立即落盘，避免等待防抖计时器。
                string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
                _apiKeySaveTimer.Stop();
                SaveCurrentApiKey();

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

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string theme })
            {
                return;
            }

            AppSettings.Save("AppTheme", theme);
            App.ApplyThemePreference(theme);
        }

        private void ReduceMotionSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            bool reduceMotion = ReduceMotionSwitch.IsOn;
            AppSettings.Save("ReduceMotion", reduceMotion);
            WeakReferenceMessenger.Default.Send(new ToggleReducedMotionMessage(reduceMotion));
        }

        private void VersionButton_Click(object sender, RoutedEventArgs e)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastVersionTapTime > TimeSpan.FromSeconds(1.2))
            {
                _versionTapCount = 0;
            }

            _lastVersionTapTime = now;
            _versionTapCount++;

            if (_versionTapCount < 3)
            {
                return;
            }

            _versionTapCount = 0;
            if (App.CurrentWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToDevLogPage();
            }
        }

        // --- MCP Server Management UI ---
        public ObservableCollection<McpServerUIModel> McpServers { get; } = new();

        private void RefreshMcpServersList()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                McpServers.Clear();
                foreach (var server in _mcpManager.GetServers())
                {
                    bool isRunning = _mcpManager.IsServerRunning(server.Id);
                    McpServers.Add(new McpServerUIModel
                    {
                        Id = server.Id,
                        Name = server.Name,
                        Command = $"{server.Command} {server.Arguments}",
                        ActionText = isRunning ? "停止" : "启动",
                        StatusText = isRunning ? "● 运行中" : "○ 已停止",
                        StatusColor = (Brush)Application.Current.Resources[
                            isRunning ? "AccentSafe" : "TextMuted"]
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
                PrimaryButtonText = "确认添加",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
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
            panel.Children.Add(new TextBlock
            {
                Text = "安全提示：MCP 是可在本机运行的第三方程序。仅添加你信任的来源。添加后不会自动启动。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["AccentReview"],
                Margin = new Thickness(0, 0, 0, 14)
            });
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
                    IsEnabled = false,
                    IsApproved = true
                };

                try
                {
                    _mcpManager.AddOrUpdateServer(config);
                }
                catch (Exception ex)
                {
                    await new ContentDialog
                    {
                        Title = "无法添加服务器",
                        Content = ex.Message,
                        CloseButtonText = "确定",
                        XamlRoot = XamlRoot
                    }.ShowAsync();
                }
            }
        }

        private async void ToggleMcpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id)
            {
                return;
            }

            if (_mcpManager.IsServerRunning(id))
            {
                _mcpManager.StopServer(id);
                return;
            }

            McpServerConfig? config = _mcpManager.GetServers().FirstOrDefault(server => server.Id == id);
            if (config == null)
            {
                return;
            }

            var confirmDialog = new ContentDialog
            {
                Title = $"启动 {config.Name}？",
                Content = $"蓝宝石将启动第三方进程：\n{config.Command} {config.Arguments}",
                PrimaryButtonText = "启动",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _mcpManager.StartServerAsync(id);
            if (!_mcpManager.IsServerRunning(id))
            {
                await new ContentDialog
                {
                    Title = "启动失败",
                    Content = "服务器未能建立连接，请检查运行环境、命令和参数。",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
        }

        private void DeleteMcpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                _mcpManager.RemoveServer(id);
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
                    addedSkill.UseDomesticNetwork = useDomestic;
                    skillManager.SaveConfig();
                    await new ContentDialog
                    {
                        Title = "规范验证完成",
                        Content = "技能已保存为“待审核”，尚未加入 AI 工具列表。请在列表中选择“审核并启用”。",
                        CloseButtonText = "确定",
                        XamlRoot = XamlRoot
                    }.ShowAsync();
                }
            }
        }

        private void DeleteSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var skillManager = App.Current.Services.GetRequiredService<WebSkillManager>();
                skillManager.RemoveSkill(id);
            }
        }

        private async void ToggleWebSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string id }) return;
            var manager = App.Current.Services.GetRequiredService<WebSkillManager>();
            WebSkillConfig? skill = manager.Skills.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (skill == null) return;

            if (skill.IsEnabled)
            {
                manager.DisableSkill(id);
                return;
            }

            (bool previewed, string previewError) = await manager.PreviewSkillAsync(id);
            if (!previewed)
            {
                await new ContentDialog
                {
                    Title = "规范验证失败",
                    Content = previewError,
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            var review = new ContentDialog
            {
                Title = $"审核 Web 技能：{skill.Name}",
                Content = new StackPanel
                {
                    Spacing = 8,
                    MinWidth = 500,
                    Children =
                    {
                        new TextBlock { Text = $"规范来源：{skill.Url}", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = $"请求目标：{skill.TargetOrigin}", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = skill.ToolCountText },
                        new TextBlock
                        {
                            Text = "启用后，AI 可看到这些第三方接口的名称和说明；实际发送参数前仍会弹出确认。请只信任你能识别的服务。",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)Application.Current.Resources["AccentReview"]
                        }
                    }
                },
                PrimaryButtonText = "信任并启用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await review.ShowAsync() != ContentDialogResult.Primary) return;

            (bool enabled, string error) = await manager.EnableSkillAsync(id);
            if (!enabled)
            {
                await new ContentDialog
                {
                    Title = "启用失败",
                    Content = error,
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
        }

        private async void AddAgentSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            var urlBox = new TextBox
            {
                PlaceholderText = "https://github.com/owner/repo/tree/main/skill",
                MinWidth = 420
            };
            var dialog = new ContentDialog
            {
                Title = "下载 Agent 技能",
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "请输入可信来源的 SKILL.md 或 GitHub 技能目录地址。下载后仍需审核才能启用。",
                            TextWrapping = TextWrapping.Wrap
                        },
                        urlBox
                    }
                },
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
                string.IsNullOrWhiteSpace(urlBox.Text))
            {
                return;
            }

            var proxyDialog = new Views.SkillProxyConfigDialog("Agent 技能")
            {
                XamlRoot = XamlRoot
            };
            await proxyDialog.ShowAsync();
            if (!proxyDialog.Result.HasValue)
            {
                return;
            }

            try
            {
                var manager = App.Current.Services.GetRequiredService<AgentSkillManager>();
                bool added = await manager.AddSkillAsync(urlBox.Text.Trim(), proxyDialog.Result.Value);
                await new ContentDialog
                {
                    Title = added ? "下载完成" : "无法识别技能",
                    Content = added
                        ? "技能已下载并保持“待审核”状态。请在列表中选择“审核并启用”。"
                        : "返回内容不像有效的 SKILL.md。",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    Title = "下载失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                }.ShowAsync();
            }
        }

        private async void ReviewAgentSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string id })
            {
                return;
            }

            var manager = App.Current.Services.GetRequiredService<AgentSkillManager>();
            AgentSkillConfig? skill = manager.Skills.FirstOrDefault(item => item.Id == id);
            if (skill == null) return;

            if (skill.IsEnabled)
            {
                skill.IsEnabled = false;
                manager.SaveConfig();
                return;
            }

            string preview = skill.Instructions.Length > 6000
                ? skill.Instructions[..6000] + "\n\n……预览已截断"
                : skill.Instructions;
            var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
            panel.Children.Add(new TextBlock { Text = $"来源：{skill.Url}", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = skill.Description, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock
            {
                Text = "以下是第三方指令预览。请确认它与预期用途一致，且不要求泄露数据、绕过确认或执行无关操作：",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["AccentReview"]
            });
            var previewBox = new TextBox
            {
                Text = preview,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 320
            };
            ScrollViewer.SetVerticalScrollBarVisibility(previewBox, ScrollBarVisibility.Auto);
            panel.Children.Add(previewBox);

            var dialog = new ContentDialog
            {
                Title = $"审核技能：{skill.Name}",
                Content = panel,
                PrimaryButtonText = "信任并启用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                skill.IsTrusted = true;
                skill.IsEnabled = true;
                manager.SaveConfig();
            }
        }

        private void DeleteAgentSkillBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string id })
            {
                App.Current.Services.GetRequiredService<AgentSkillManager>().RemoveSkill(id);
            }
        }
    }

    public class McpServerUIModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string ActionText { get; set; } = "启动";
        public Brush StatusColor { get; set; } =
            (Brush)Application.Current.Resources["TextMuted"];
    }
}

