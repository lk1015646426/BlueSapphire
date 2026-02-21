using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BlueSapphire.Models
{
    // 定义一个简单的消息，携带字符串 (日志标题)
    public class DevLogCompletedMessage : ValueChangedMessage<string>
    {
        public DevLogCompletedMessage(string logTitle) : base(logTitle) { }
    }

    // 用于粒子特效开关的消息 (继承自 ValueChangedMessage<bool>)
    public class ToggleParticleMessage : ValueChangedMessage<bool>
    {
        public ToggleParticleMessage(bool isEnabled) : base(isEnabled) { }
    }
}