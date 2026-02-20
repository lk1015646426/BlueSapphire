using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BlueSapphire.Models
{
    // 定义一个简单的消息，携带布尔值 (true/false)
    public class DevLogCompletedMessage : ValueChangedMessage<string>
    {
        public DevLogCompletedMessage(string logTitle) : base(logTitle) { }
    }
}