using Microsoft.UI.Xaml.Controls;
using System.Reflection;

namespace BlueSapphire
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                .Split('+')[0]
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                ?? "未知";
            VersionText.Text = $"版本 {version}";
        }
    }
}
