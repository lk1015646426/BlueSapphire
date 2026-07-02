using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BlueSapphire.Views
{
    public sealed partial class SkillProxyConfigDialog : ContentDialog
    {
        public bool? Result { get; private set; } = null;

        public SkillProxyConfigDialog(string skillName)
        {
            this.InitializeComponent();
            SubtitleText.Text = $"选择 [{skillName}] 的连接方式";
        }

        private void Domestic_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.Hide();
        }

        private void SystemProxy_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.Hide();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            this.Hide();
        }
    }
}
