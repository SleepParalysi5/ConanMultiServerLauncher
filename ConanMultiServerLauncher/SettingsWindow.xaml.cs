using System.Windows;
using ConanMultiServerLauncher.Services;

namespace ConanMultiServerLauncher
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = SettingsService.Load();
            CloseAfterLaunchCheck.IsChecked = _settings.CloseLauncherAfterLaunch;
            WriteModListOnChangeCheck.IsChecked = _settings.WriteModListOnProfileChange;
            TextureStreamingCheck.IsChecked = _settings.TextureStreamingEnabled;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _settings.CloseLauncherAfterLaunch = CloseAfterLaunchCheck.IsChecked == true;
            _settings.WriteModListOnProfileChange = WriteModListOnChangeCheck.IsChecked == true;
            _settings.TextureStreamingEnabled = TextureStreamingCheck.IsChecked != false; // default to true when null
            SettingsService.Save(_settings);
            DialogResult = true;
            Close();
        }
    }
}