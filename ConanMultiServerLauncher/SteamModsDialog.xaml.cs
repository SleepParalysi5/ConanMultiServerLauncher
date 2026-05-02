using System.Windows;

namespace ConanMultiServerLauncher
{
    public partial class SteamModsDialog : Window
    {
        public string ModsText { get; private set; } = string.Empty;

        public SteamModsDialog()
        {
            InitializeComponent();
            ModsTextBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ModsText = ModsTextBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
