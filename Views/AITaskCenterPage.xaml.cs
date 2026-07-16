using BlueSapphire.Models;
using BlueSapphire.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;

namespace BlueSapphire.Views
{
    public sealed partial class AITaskCenterPage : Page
    {
        private readonly AITaskCenterService _taskCenter;

        public ObservableCollection<AITaskRecord> Tasks { get; } = new();
        public Visibility IsEmpty => Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasTasks => Tasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public AITaskCenterPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeComponent();
            _taskCenter = App.Current.Services.GetRequiredService<AITaskCenterService>();
            _taskCenter.TasksChanged += TaskCenter_TasksChanged;
            RefreshTasks();
        }

        private void TaskCenter_TasksChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(RefreshTasks);
        }

        private void RefreshTasks()
        {
            Tasks.Clear();
            foreach (AITaskRecord task in _taskCenter.GetSnapshot())
            {
                Tasks.Add(task);
            }
            Bindings.Update();
        }

        private void CancelTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string taskId })
            {
                _taskCenter.Cancel(taskId);
            }
        }

        private void ClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            _taskCenter.RemoveCompleted();
        }
    }
}
