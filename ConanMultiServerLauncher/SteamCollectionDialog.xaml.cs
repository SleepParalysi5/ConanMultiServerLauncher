using System.Windows;

namespace ConanMultiServerLauncher
{
    public partial class SteamCollectionDialog : Window
    {
        public string CollectionText { get; private set; } = string.Empty;

        public SteamCollectionDialog()
        {
            InitializeComponent();
            UrlTextBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            CollectionText = UrlTextBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
