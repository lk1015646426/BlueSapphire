using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using BlueSapphire.Helpers;
using BlueSapphire.Models;
using CommunityToolkit.Mvvm.Messaging;

namespace BlueSapphire
{
    public sealed partial class HomePage : Page
    {
        private bool _isLoaded;
        public HomePage()
        {
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<ToggleReducedMotionMessage>(
                this,
                (_, message) => DispatcherQueue.TryEnqueue(() =>
                    ApplyReducedMotion(message.Value)));
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            UpdateHourlyGreeting();
            ApplyReducedMotion(AppSettings.Get("ReduceMotion", false));
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            CoreIdleAnimation.Stop();
            EntranceStoryboard.Stop();
        }

        private void ApplyReducedMotion(bool reduceMotion)
        {
            if (!_isLoaded) return;
            if (reduceMotion)
            {
                CoreIdleAnimation.Stop();
                EntranceStoryboard.Stop();
                ContentStack.Opacity = 1;
                ContentTranslate.Y = 0;
                CoreScale.ScaleX = 1;
                CoreScale.ScaleY = 1;
                CoreTranslate.Y = 0;
                OuterRing.Opacity = 0.12;
                RingScale.ScaleX = 1;
                RingScale.ScaleY = 1;
                return;
            }

            CoreIdleAnimation.Begin();
            EntranceStoryboard.Begin();
        }

        private void UpdateHourlyGreeting()
        {
            int hour = DateTime.Now.Hour;
            GreetingText.Text = hour switch
            {
                >= 5 and < 9 => "早上好，今天想先处理什么？",
                >= 9 and < 12 => "上午好，从最重要的一项开始。",
                >= 12 and < 14 => "中午好，可以先做一次轻量整理。",
                >= 14 and < 18 => "下午好，继续处理手头的任务。",
                >= 18 and < 23 => "晚上好，看看还有哪些事项需要收尾。",
                _ => "夜深了，只处理最重要的事情就好。"
            };
        }
    }
}
