using BlueSapphire.Models;
using BlueSapphire.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace BlueSapphire.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly AITaskCenterService _taskCenter;
        private bool _subscribed;

        public ObservableCollection<AITaskRecord> TaskPreview { get; } = new();
        public ObservableCollection<AITaskRecord> ActivityPreview { get; } = new();
        public string TotalTaskCountText { get; private set; } = "0";
        public string ActiveTaskCountText { get; private set; } = "0";
        public Visibility EmptyVisibility { get; private set; } = Visibility.Visible;
        public Visibility TaskListVisibility { get; private set; } = Visibility.Collapsed;

        public HomePage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeComponent();
            DataContext = this;
            _taskCenter = App.Current.Services.GetRequiredService<AITaskCenterService>();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (AiFrame.Content == null)
            {
                AiFrame.Navigate(typeof(Views.AICopilotPage));
            }

            if (!_subscribed)
            {
                _taskCenter.TasksChanged += TaskCenter_TasksChanged;
                _subscribed = true;
            }
            RefreshTaskData();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_subscribed)
            {
                _taskCenter.TasksChanged -= TaskCenter_TasksChanged;
                _subscribed = false;
            }
        }

        private void TaskCenter_TasksChanged(object? sender, EventArgs e) =>
            DispatcherQueue.TryEnqueue(RefreshTaskData);

        private void RefreshTaskData()
        {
            var snapshot = _taskCenter.GetSnapshot();
            var ordered = snapshot.OrderByDescending(task => task.IsActive)
                                  .ThenByDescending(task => task.UpdatedAt)
                                  .ToList();

            TaskPreview.Clear();
            foreach (AITaskRecord task in ordered.Take(3)) TaskPreview.Add(task);
            ActivityPreview.Clear();
            foreach (AITaskRecord task in snapshot.OrderByDescending(task => task.UpdatedAt).Take(3))
                ActivityPreview.Add(task);

            TotalTaskCountText = snapshot.Count.ToString();
            ActiveTaskCountText = snapshot.Count(task => task.IsActive).ToString();
            bool isEmpty = snapshot.Count == 0;
            EmptyVisibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            TaskListVisibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            DataContext = null;
            DataContext = this;
        }

        private static MainWindow? Shell => App.CurrentWindow as MainWindow;
        private void Media_Click(object sender, RoutedEventArgs e) => Shell?.NavigateToTool("MediaManager");
        private void Cleaner_Click(object sender, RoutedEventArgs e) => Shell?.NavigateToTool("CleanerAssistant");

        private void TaskOpen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string kind }) return;
            if (kind.Contains("media", StringComparison.OrdinalIgnoreCase)) Shell?.NavigateToTool("MediaManager");
            else if (kind.Contains("clean", StringComparison.OrdinalIgnoreCase)) Shell?.NavigateToTool("CleanerAssistant");
        }
    }
}
