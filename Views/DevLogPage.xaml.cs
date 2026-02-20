using Microsoft.UI.Xaml.Controls;
using BlueSapphire.ViewModels;

namespace BlueSapphire.Views
{
    public sealed partial class DevLogPage : Page
    {
        public DevLogViewModel ViewModel { get; } = new DevLogViewModel();

        public DevLogPage()
        {
            this.InitializeComponent();
            this.Name = "RootPage"; // 用于在 ListView 内部绑定 ViewModel 的 Command
        }
    }
}