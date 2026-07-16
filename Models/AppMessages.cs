using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BlueSapphire.Models
{
    // 定义一个简单的消息，携带字符串 (日志标题)
    public class DevLogCompletedMessage : ValueChangedMessage<string>
    {
        public DevLogCompletedMessage(string logTitle) : base(logTitle) { }
    }

    public class ToggleParticleMessage : ValueChangedMessage<bool>
    {
        public ToggleParticleMessage(bool isEnabled) : base(isEnabled) { }
    }

    public class ToggleReducedMotionMessage : ValueChangedMessage<bool>
    {
        public ToggleReducedMotionMessage(bool reduceMotion) : base(reduceMotion) { }
    }

    public class ShowTipMessage
    {
        public string Title { get; }
        public string Message { get; }

        public ShowTipMessage(string title, string message)
        {
            Title = title;
            Message = message;
        }
    }

    public class RunAutomaticLowRiskCleanupMessage
    {
    }

    public class StartQuickScanMessage
    {
    }

    public class RunCleanupMessage
    {
    }
}
