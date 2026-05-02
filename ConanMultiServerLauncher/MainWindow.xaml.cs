using ConanMultiServerLauncher.Models;
using ConanMultiServerLauncher.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using System.IO;

namespace ConanMultiServerLauncher
{
    public partial class MainWindow : Window
    {
        private readonly ProfilesService _profilesService = new();
        private ObservableCollection<Profile> _profiles = new();
        private Profile? _current;
        private bool _isInitializing = true; // suppress SelectionChanged side-effects during startup

        public MainWindow()
        {
            InitializeComponent();
            LoadProfiles();

            // Restore last selected profile
            var settingsAtStart = SettingsService.Load();
            if (!string.IsNullOrWhiteSpace(settingsAtStart.LastSelectedProfile) && _profiles.Count > 0)
            {
                var match = _profiles.FirstOrDefault(p => string.Equals(p.Name, settingsAtStart.LastSelectedProfile, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ProfilesListBox.SelectedItem = match;
                    SetCurrent(match);
                }
            }

            RefreshModListPathLabel();

            // Finish initialization; from now on SelectionChanged should persist settings
            _isInitializing = false;
        }

        private void LoadProfiles()
        {
            _profiles = new ObservableCollection<Profile>(_profilesService.Load());
            ProfilesListBox.ItemsSource = _profiles;
            ProfilesListBox.IsEnabled = _profiles.Count > 0;
            if (_profiles.Count > 0)
            {
                ProfilesListBox.SelectedIndex = 0;
                SetCurrent(_profiles[0]);
            }
            else
            {
                _current = null;
            }
        }

        private void SetCurrent(Profile p)
        {
            _current = p;
            
            // Trigger async update check for current profile
            _ = CheckForModUpdatesAsync(p);
        }

        private async System.Threading.Tasks.Task<bool> CheckForModUpdatesAsync(Profile? p)
        {
            if (p == null || p.ModIds.Count == 0) return false;

            p.IsUpdating = true;
            bool anyUpdate = false;
            try
            {
                var modIds = p.ModIds.ToList();
                var remoteInfos = await SteamWorkshopService.GetModsUpdateInfoAsync(modIds);
                
                foreach (var info in remoteInfos)
                {
                    var localPath = ModListService.TryGetPakPathForId(info.PublishedFileId);
                    if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) continue;

                    var localLastUpdate = new DateTimeOffset(File.GetLastWriteTimeUtc(localPath)).ToUnixTimeSeconds();
                    bool needsUpdate = (long)info.TimeUpdated > localLastUpdate;
                    if (needsUpdate) anyUpdate = true;
                }
            }
            catch { /* Ignore background check errors */ }
            finally
            {
                p.IsUpdating = false;
            }

            p.NeedsUpdate = anyUpdate;
            return anyUpdate;
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            // Generate a unique default name
            var baseName = "New Profile";
            var name = baseName;
            int i = 1;
            var existingNames = new HashSet<string>(_profiles.Select(pr => pr.Name), StringComparer.OrdinalIgnoreCase);
            while (existingNames.Contains(name))
            {
                name = $"{baseName} {i++}";
            }

            var p = new Profile { Name = name };
            _profiles.Add(p);
            _profilesService.Save(_profiles.ToList());
            ProfilesListBox.SelectedItem = p;
            SetCurrent(p);

            // Open edit window for the new profile
            OpenEditWindow(p);
        }

        private void EditProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Profile p)
            {
                OpenEditWindow(p);
            }
        }

        private async void ForceUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Profile p)
            {
                var originalContent = btn.Content;
                btn.Content = "...";
                btn.IsEnabled = false;
                try
                {
                    await CheckForModUpdatesAsync(p);
                }
                finally
                {
                    btn.Content = originalContent;
                    btn.IsEnabled = true;
                }
            }
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Profile p)
            {
                if (System.Windows.MessageBox.Show($"Delete profile '{p.Name}'?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _profiles.Remove(p);
                    _profilesService.Save(_profiles.ToList());
                    LoadProfiles();
                }
            }
        }

        private void OpenEditWindow(Profile p)
        {
            var dlg = new EditProfileWindow(p) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _profilesService.Save(_profiles.ToList());
                LoadProfiles(); // Refresh list
            }
        }

        private void RefreshModListPathLabel()
        {
            // Path labels removed from UI
        }

        private void LocateModList_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Conan servermodlist.txt",
                Filter = "servermodlist.txt|servermodlist.txt|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                var settings = SettingsService.Load();
                settings.ModListTxtOverride = ofd.FileName;
                try
                {
                    var sandbox = System.IO.Path.GetDirectoryName(ofd.FileName);
                    if (!string.IsNullOrWhiteSpace(sandbox))
                    {
                        var modsDir = System.IO.Path.Combine(sandbox!, "Mods");
                        if (System.IO.Directory.Exists(modsDir))
                            settings.ConanModsFolderOverride = modsDir;
                    }
                }
                catch { }
                SettingsService.Save(settings);
                RefreshModListPathLabel();
                System.Windows.MessageBox.Show("servermodlist location saved.");
            }
        }

        private void LocateWorkshop_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Steam Workshop folder for Conan (…\\steamapps\\workshop\\content\\440900)"
            };
            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var selected = dlg.SelectedPath;
                var settings = SettingsService.Load();
                settings.Workshop440900Override = selected;
                SettingsService.Save(settings);
                System.Windows.MessageBox.Show("Workshop path saved.");
            }
        }

        private async void WriteModList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_current == null) return;
                await ModListService.WriteConanModListTxtAsync(_current.ModIds);
                System.Windows.MessageBox.Show("servermodlist.txt and modlist.txt updated.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to write servermodlist.txt: {ex.Message}\n\nTip: Click 'Locate servermodlist.txt' to set its location if auto-detection fails.");
            }
        }

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_current == null)
                {
                    System.Windows.MessageBox.Show("Create or select a profile first.");
                    return;
                }

                // Check for updates/downloads before launch
                var missingIds = _current.ModIds.Where(id => ModListService.TryGetPakPathForId(id) == null).ToList();
                if (missingIds.Count > 0)
                {
                    var result = System.Windows.MessageBox.Show($"{missingIds.Count} mods in this profile are not installed. Would you like to download them now via SteamCMD?", "Download Missing Mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            await SteamCmdService.DownloadModsAsync(missingIds, msg => Debug.WriteLine($"[SteamCMD] {msg}"));
                            SetCurrent(_current);
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show($"Download failed: {ex.Message}");
                            return; // Don't launch if download failed and was requested
                        }
                    }
                }

                await CheckForModUpdatesAsync(_current);

                // Ensure modlist is written before launch
                await ModListService.WriteConanModListTxtAsync(_current.ModIds);

                // Update last-connected server in config files so the game can connect via -continuesession
                if (string.Equals(_current.ServerAddress, "singleplayer", StringComparison.OrdinalIgnoreCase))
                {
                    await GameConfigService.UpdateSingleplayerModeAsync();
                }
                else
                {
                    // Even if address is empty, we update (passing empty strings) to clear old values
                    await GameConfigService.UpdateLastConnectedServerAsync(_current.ServerAddress, _current.Password);
                }

                await LauncherService.LaunchConanAsync(_current.BattlEyeEnabled, _current.ServerAddress, _current.Password);

                // Close after launch if enabled in settings
                var settings = SettingsService.Load();
                if (settings.CloseLauncherAfterLaunch)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to launch Conan: {ex.Message}");
            }
        }

        private async void ProfilesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return; // ignore events during startup

            if (ProfilesListBox.SelectedItem is Profile p)
            {
                SetCurrent(p);

                // Persist last selected profile
                var settings = SettingsService.Load();
                if (!string.Equals(settings.LastSelectedProfile, p.Name, StringComparison.Ordinal))
                {
                    settings.LastSelectedProfile = p.Name;
                    SettingsService.Save(settings);
                }

                // Update mod list and server config on profile change
                if (_current != null)
                {
                    try
                    {
                        // Always update server config so "Launch" can continue session
                        if (string.Equals(_current.ServerAddress, "singleplayer", StringComparison.OrdinalIgnoreCase))
                        {
                            await GameConfigService.UpdateSingleplayerModeAsync();
                        }
                        else
                        {
                            // Even if address is empty, we update (passing empty strings) to clear old values
                            await GameConfigService.UpdateLastConnectedServerAsync(_current.ServerAddress, _current.Password);
                        }

                        // Write mod list
                        await ModListService.WriteConanModListTxtAsync(_current.ModIds);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProfileChange] Failed to update config: {ex.Message}");
                    }
                }
            }
        }

        private void ConnectProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Profile p)
            {
                SetCurrent(p);
                Launch_Click(sender, e);
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow { Owner = this };
            dlg.ShowDialog();
        }

        private void KillConan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Target process names to terminate (without .exe)
                string[] targetNames = new[]
                {
                    "ConanSandbox",       // Main game
                    "ConanSandbox_BE",    // BattlEye launcher for Conan
                    "BEService",          // BattlEye service (some installs)
                    "BEService_x64",      // BattlEye service we may have started
                    "steamcmd"            // SteamCMD if it hangs
                };

                foreach (var name in targetNames)
                {
                    try
                    {
                        var byName = Process.GetProcessesByName(name);
                        foreach (var p in byName)
                        {
                            try { p.Kill(true); }
                            catch { try { p.Kill(); } catch { } }
                        }
                    }
                    catch { /* swallow per requirement */ }
                }

                // Additionally, try to find any visible window titled like BattlEye Launcher and kill it
                try
                {
                    var all = Process.GetProcesses();
                    foreach (var p in all)
                    {
                        try
                        {
                            var title = p.MainWindowTitle;
                            if (!string.IsNullOrWhiteSpace(title) && title.IndexOf("battleye launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                try { p.Kill(true); }
                                catch { try { p.Kill(); } catch { } }
                            }
                        }
                        catch { /* accessing MainWindowTitle can fail on some system processes */ }
                    }
                }
                catch { }

                // No confirmation or message boxes by design per requirement
            }
            catch
            {
                // Swallow all exceptions to avoid any confirmation or alerts
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set minimum width so that all current content (especially the buttons) remains visible horizontally
            this.MinWidth = this.ActualWidth;

            try { RunStartupCheck(); } catch { }
            
            // Check all profiles for updates on start
            _ = CheckAllProfilesForUpdatesAsync();
        }

        private async System.Threading.Tasks.Task CheckAllProfilesForUpdatesAsync()
        {
            var tasks = _profiles.Select(p => CheckForModUpdatesAsync(p)).ToList();
            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        private void RunStartupCheck()
        {
            var settings = SettingsService.Load();

            // Evaluate required components
            var missing = new List<string>();

            var steamExe = PathsService.GetSteamExe();
            if (string.IsNullOrWhiteSpace(steamExe) || !File.Exists(steamExe))
                missing.Add("Steam executable (steam.exe)");

            var conanRoot = PathsService.GetConanRoot();
            if (string.IsNullOrWhiteSpace(conanRoot) || !Directory.Exists(conanRoot))
            {
                missing.Add("Conan Exiles installation folder");
            }
            else
            {
                var bin64 = Path.Combine(conanRoot!, "ConanSandbox", "Binaries", "Win64");
                var exeConan = Path.Combine(bin64, "ConanSandbox.exe");
                var exeConanBE = Path.Combine(bin64, "ConanSandbox_BE.exe");
                if (!File.Exists(exeConan)) missing.Add("ConanSandbox.exe");
                if (!File.Exists(exeConanBE)) missing.Add("ConanSandbox_BE.exe");

                // BattlEye service is optional for launch; we won't fail on it.
            }

            // Funcom launcher exe (optional to use; include informationally but do not block)
            var funcomLauncher = PathsService.GetFuncomLauncherExe();
            // Do not treat as missing requirement, but could be useful info; we skip adding to missing.

            // Mod list locations
            var serverModList = PathsService.GetConanServerModListTxt();
            if (string.IsNullOrWhiteSpace(serverModList))
                missing.Add("ConanSandbox\\servermodlist.txt location");
            else
            {
                var dir = Path.GetDirectoryName(serverModList)!;
                if (!Directory.Exists(dir)) missing.Add("ConanSandbox folder for servermodlist.txt");
            }

            var localModList = PathsService.GetLocalAppDataModListTxt();
            var localDir = Path.GetDirectoryName(localModList)!;
            if (!Directory.Exists(localDir)) missing.Add("LocalAppData modlist.txt folder");

            // Workshop folder (optional but useful for mods resolution)
            var workshop = PathsService.GetWorkshopContent440900();
            if (string.IsNullOrWhiteSpace(workshop) || !Directory.Exists(workshop))
            {
                // This is not strictly required to run, but is important for mods browsing; include as missing info.
                missing.Add("Steam Workshop content 440900 folder");
            }

            // Message logic per requirement
            var isFirstRun = !settings.HasShownStartupCheck;
            if (isFirstRun)
            {
                if (missing.Count == 0)
                {
                    System.Windows.MessageBox.Show("Environment check successful. All required executables and mod list locations were found.", "Initial setup", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var msg = "Some items could not be found:\n - " + string.Join("\n - ", missing) +
                              "\n\nYou can use the 'Locate servermodlist.txt' and 'Locate Workshop 440900' buttons in the Mods section to set paths.";
                    System.Windows.MessageBox.Show(msg, "Initial setup issues", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                settings.HasShownStartupCheck = true;
                SettingsService.Save(settings);
            }
            else
            {
                if (missing.Count > 0)
                {
                    var msg = "Some items could not be found:\n - " + string.Join("\n - ", missing) +
                              "\n\nYou will only be notified when something is missing. Use 'Locate…' buttons in the Mods section to configure paths.";
                    System.Windows.MessageBox.Show(msg, "Environment check", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
