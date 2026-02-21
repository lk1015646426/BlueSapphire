using Microsoft.UI.Xaml.Controls;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogInputDialog : ContentDialog
    {
        // 这三个属性用于把用户输入的数据传递给外面的 DevLogPage
        public string NodeTitle => TitleInput.Text;
        public string NodeDescription => DescInput.Text;
        public string NodeVersion => VersionInput.Text;

        public DevLogInputDialog()
        {
            this.InitializeComponent();
        }
    }
}