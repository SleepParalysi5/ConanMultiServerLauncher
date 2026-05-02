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
            
            var currentPath = _settings.SteamCmdPath;
            if (string.IsNullOrWhiteSpace(currentPath) || !System.IO.File.Exists(currentPath))
            {
                currentPath = SteamCmdService.GetSteamCmdPath();
            }
            SteamCmdPathBox.Text = currentPath ?? string.Empty;
        }

        private void BrowseSteamCmd_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "steamcmd.exe|steamcmd.exe|Executable Files (*.exe)|*.exe",
                Title = "Select steamcmd.exe"
            };
            if (ofd.ShowDialog() == true)
            {
                SteamCmdPathBox.Text = ofd.FileName;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _settings.CloseLauncherAfterLaunch = CloseAfterLaunchCheck.IsChecked == true;
            _settings.WriteModListOnProfileChange = WriteModListOnChangeCheck.IsChecked == true;
            _settings.TextureStreamingEnabled = TextureStreamingCheck.IsChecked != false; // default to true when null
            _settings.SteamCmdPath = string.IsNullOrWhiteSpace(SteamCmdPathBox.Text) ? null : SteamCmdPathBox.Text.Trim();
            SettingsService.Save(_settings);
            DialogResult = true;
            Close();
        }
    }
}