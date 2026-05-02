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
        private ObservableCollection<ModItem> _currentMods = new();
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
                    ProfilesCombo.SelectedItem = match;
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
            ProfilesCombo.ItemsSource = _profiles;
            ProfilesCombo.IsEnabled = _profiles.Count > 0;
            if (_profiles.Count > 0)
            {
                ProfilesCombo.SelectedIndex = 0;
                SetCurrent(_profiles[0]);
            }
            else
            {
                _current = null;
                ProfileName.Text = string.Empty;
                ServerAddress.Text = string.Empty;
                ServerPassword.Password = string.Empty;
                _currentMods.Clear();
                ModsList.ItemsSource = _currentMods;
                ModsCountText.Text = "Mods: 0";
                if (BattlEyeCheckbox != null) BattlEyeCheckbox.IsChecked = false;
            }
        }

        private void SetCurrent(Profile p)
        {
            _current = p;
            ProfileName.Text = p.Name;
            ServerAddress.Text = p.ServerAddress ?? string.Empty;
            ServerPassword.Password = p.Password ?? string.Empty;
            
            _currentMods.Clear();
            foreach (var id in p.ModIds)
            {
                _currentMods.Add(new ModItem 
                { 
                    PublishedFileId = id, 
                    DisplayLabel = ModListService.GetDisplayLabelForId(id) 
                });
            }
            ModsList.ItemsSource = _currentMods;
            
            ModsCountText.Text = $"Mods: {p.ModIds.Count}";
            if (BattlEyeCheckbox != null) BattlEyeCheckbox.IsChecked = p.BattlEyeEnabled;

            // Trigger async update check
            _ = CheckForModUpdatesAsync();
        }

        private async System.Threading.Tasks.Task<bool> CheckForModUpdatesAsync()
        {
            if (_current == null || _current.ModIds.Count == 0) return false;

            bool anyUpdate = false;
            try
            {
                var modIds = _current.ModIds.ToList();
                var remoteInfos = await SteamWorkshopService.GetModsUpdateInfoAsync(modIds);
                
                foreach (var info in remoteInfos)
                {
                    var localPath = ModListService.TryGetPakPathForId(info.PublishedFileId);
                    if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) continue;

                    var localLastUpdate = new DateTimeOffset(File.GetLastWriteTimeUtc(localPath)).ToUnixTimeSeconds();
                    bool needsUpdate = (long)info.TimeUpdated > localLastUpdate;
                    if (needsUpdate) anyUpdate = true;

                    var uiMod = _currentMods.FirstOrDefault(m => m.PublishedFileId == info.PublishedFileId);
                    if (uiMod != null)
                    {
                        uiMod.NeedsUpdate = needsUpdate;
                    }
                }
            }
            catch { /* Ignore background check errors */ }

            return anyUpdate;
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            // Generate a unique default name to avoid accidental overwrites when saving (upsert-by-name)
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
            ProfilesCombo.SelectedItem = p;
            SetCurrent(p);
            ProfileName.Text = p.Name;
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            // Upsert logic: if a profile with this name exists, override it; otherwise update current or add new.
            var name = ProfileName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                System.Windows.MessageBox.Show("Profile Name cannot be empty.");
                return;
            }

            var serverAddr = string.IsNullOrWhiteSpace(ServerAddress.Text) ? null : ServerAddress.Text.Trim();
            var password = string.IsNullOrWhiteSpace(ServerPassword.Password) ? null : ServerPassword.Password;
            var mods = _currentMods.Select(m => m.PublishedFileId).Distinct().ToList();

            var existingWithNewName = _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (_current != null)
            {
                // We have a currently selected profile (could be a "New Profile" dummy or an existing one)
                if (!string.Equals(_current.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    // User changed the name in the text box.
                    if (existingWithNewName != null && existingWithNewName != _current)
                    {
                        // The NEW name belongs to ANOTHER existing profile.
                        var result = System.Windows.MessageBox.Show($"A profile named '{name}' already exists. Overwrite it with these settings?", "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result != MessageBoxResult.Yes) return;

                        // Remove the other profile, we will update the current one to this name.
                        _profiles.Remove(existingWithNewName);
                    }
                    _current.Name = name;
                }
            }
            else
            {
                // No profile selected (e.g. all deleted), create a new one.
                if (existingWithNewName != null)
                {
                    _current = existingWithNewName;
                }
                else
                {
                    _current = new Profile { Name = name };
                    _profiles.Add(_current);
                }
            }

            // Update fields
            _current.ServerAddress = serverAddr;
            _current.Password = password;
            _current.ModIds = mods;
            _current.BattlEyeEnabled = BattlEyeCheckbox?.IsChecked == true;

            // Refresh UI
            ProfilesCombo.SelectedItem = _current;
            SetCurrent(_current);

            // Persist
            _profilesService.Save(_profiles.ToList());
            System.Windows.MessageBox.Show("Profile saved.");
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            if (System.Windows.MessageBox.Show($"Delete profile '{_current.Name}'?", "Confirm", System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes)
            {
                _profiles.Remove(_current);
                _profilesService.Save(_profiles.ToList());
                if (_profiles.Count > 0)
                {
                    ProfilesCombo.SelectedIndex = 0;
                    SetCurrent(_profiles[0]);
                }
                else
                {
                    _current = null;
                    ProfileName.Text = string.Empty;
                    ServerAddress.Text = string.Empty;
                    ServerPassword.Password = string.Empty;
                    _currentMods.Clear();
                    ModsList.ItemsSource = _currentMods;
                    ModsCountText.Text = "Mods: 0";
                }
            }
        }

        private async void PasteMods_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            
            var dialog = new SteamModsDialog { Owner = this };
            if (System.Windows.Clipboard.ContainsText())
            {
                dialog.ModsTextBox.Text = System.Windows.Clipboard.GetText();
                dialog.ModsTextBox.SelectAll();
            }

            if (dialog.ShowDialog() != true) return;
            var text = dialog.ModsText;

            // Use tolerant parser: supports URLs, .pak filenames, and workshop paths
            var ids = ModListService.ExtractModIdsFromAny(text);
            if (ids.Count == 0)
            {
                System.Windows.MessageBox.Show("No Workshop IDs found. Tip: paste Steam Workshop URLs/IDs, .pak filenames (e.g. workshop_1234567890.pak), or full .pak paths.");
                return;
            }
            
            MergeMods(ids);

            // Download missing mods
            var missingIds = ids.Where(id => ModListService.TryGetPakPathForId(id) == null).ToList();
            if (missingIds.Count > 0)
            {
                var result = System.Windows.MessageBox.Show($"{missingIds.Count} mods are not installed. Would you like to download them now via SteamCMD?", "Download Missing Mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                        await SteamCmdService.DownloadModsAsync(missingIds, msg => {
                            Debug.WriteLine($"[SteamCMD] {msg}");
                        });
                        System.Windows.MessageBox.Show("Download complete.");
                        // Refresh mod list to update "not installed" labels
                        SetCurrent(_current);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Download failed: {ex.Message}\n\nMake sure steamcmd.exe path is set in settings.");
                    }
                    finally
                    {
                        System.Windows.Input.Mouse.OverrideCursor = null;
                    }
                }
            }
        }

        private void LoadModsFromFile_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                var ids = ModListService.ReadIdsFromTextFile(ofd.FileName);
                if (ids.Count == 0)
                {
                    System.Windows.MessageBox.Show("No Workshop IDs found in file. Tip: include URLs/IDs or .pak filenames/paths.");
                    return;
                }
                MergeMods(ids);
            }
        }

        private async void PasteCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            
            var dialog = new SteamCollectionDialog { Owner = this };
            if (System.Windows.Clipboard.ContainsText())
            {
                dialog.UrlTextBox.Text = System.Windows.Clipboard.GetText();
                dialog.UrlTextBox.SelectAll();
            }

            if (dialog.ShowDialog() != true) return;
            var text = dialog.CollectionText;

            if (!SteamWorkshopService.TryExtractCollectionId(text, out var collectionId))
            {
                System.Windows.MessageBox.Show("Could not extract a Steam Workshop collection URL/ID from the input.");
                return;
            }
            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var ids = await SteamWorkshopService.GetCollectionChildrenAsync(collectionId);
                // Optionally filter to Conan Exiles only
                var filtered = await SteamWorkshopService.FilterToConanAsync(ids);
                var finalIds = filtered.Count > 0 ? filtered : ids;
                if (finalIds.Count == 0)
                {
                    System.Windows.MessageBox.Show("The collection contains no items (or none for Conan Exiles).");
                    return;
                }
                
                MergeMods(finalIds);

                // Download missing mods
                var missingIds = finalIds.Where(id => ModListService.TryGetPakPathForId(id) == null).ToList();
                if (missingIds.Count > 0)
                {
                    var result = System.Windows.MessageBox.Show($"{missingIds.Count} mods in this collection are not installed. Would you like to download them now via SteamCMD?", "Download Missing Mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            await SteamCmdService.DownloadModsAsync(missingIds, msg => {
                                // Simple log to debug for now
                                Debug.WriteLine($"[SteamCMD] {msg}");
                            });
                            System.Windows.MessageBox.Show("Download complete.");
                            // Refresh mod list to update "not installed" labels
                            SetCurrent(_current);
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show($"Download failed: {ex.Message}\n\nMake sure steamcmd.exe path is set in settings.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to expand collection: {ex.Message}");
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        private void ClearMods_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            _current.ModIds.Clear();
            _currentMods.Clear();
            ModsCountText.Text = "Mods: 0";
        }

        private async void CheckUpdatesManual_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _current.ModIds.Count == 0)
            {
                System.Windows.MessageBox.Show("No mods to check.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var btn = (System.Windows.Controls.Button)sender;
            var originalContent = btn.Content;
            btn.Content = "Checking...";
            btn.IsEnabled = false;

            try
            {
                bool anyUpdate = await CheckForModUpdatesAsync();
                if (!anyUpdate)
                {
                    System.Windows.MessageBox.Show("All mods are up to date.", "Check Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                btn.Content = originalContent;
                btn.IsEnabled = true;
            }
        }

        private void MergeMods(IEnumerable<long> ids)
        {
            if (_current == null) return;
            var set = new HashSet<long>(_current.ModIds);
            var added = 0;
            foreach (var id in ids)
            {
                if (set.Add(id))
                {
                    _current.ModIds.Add(id);
                    _currentMods.Add(new ModItem 
                    { 
                        PublishedFileId = id, 
                        DisplayLabel = ModListService.GetDisplayLabelForId(id) 
                    });
                    added++;
                }
            }
            _current.ModIds = _current.ModIds.Distinct().ToList();
            ModsCountText.Text = $"Mods: {_current.ModIds.Count} (added {added})";
            
            // Re-check updates for the newly added mods if needed, or just all
            _ = CheckForModUpdatesAsync();
        }

        private void RefreshModListPathLabel()
        {
            if (CurrentModListPath != null)
            {
                CurrentModListPath.Text = PathsService.GetConanServerModListTxt() ?? "servermodlist.txt: not set";
            }
            if (CurrentLocalModListPath != null)
            {
                try
                {
                    var localPath = PathsService.GetLocalAppDataModListTxt();
                    CurrentLocalModListPath.Text = string.IsNullOrWhiteSpace(localPath) ? "modlist.txt: not found" : localPath;
                }
                catch
                {
                    CurrentLocalModListPath.Text = "modlist.txt: not found";
                }
            }
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

        private void WriteModList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_current == null) return;
                ModListService.WriteConanModListTxt(_current.ModIds);
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

                await CheckForModUpdatesAsync();

                // If any mod needs update, notify user (optional, but good for UX)
                if (_currentMods.Any(m => m.NeedsUpdate))
                {
                    var result = System.Windows.MessageBox.Show("Some mods appear to have updates available (marked in orange). Launch anyway?", "Updates Available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes) return;
                }

                // Ensure modlist is written before launch
                ModListService.WriteConanModListTxt(_current.ModIds);

                // Update last-connected server in config files so the game can connect via -continuesession
                if (string.Equals(_current.ServerAddress, "singleplayer", StringComparison.OrdinalIgnoreCase))
                {
                    GameConfigService.UpdateSingleplayerMode();
                }
                else
                {
                    // Even if address is empty, we update (passing empty strings) to clear old values
                    GameConfigService.UpdateLastConnectedServer(_current.ServerAddress, _current.Password);
                }

                LauncherService.LaunchConan(_current.BattlEyeEnabled, _current.ServerAddress, _current.Password);

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

        private void OpenProfilesFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "ConanMultiServerLauncher");
                Directory.CreateDirectory(dir);
                // Open in Explorer
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open Profiles folder: {ex.Message}");
            }
        }

        private void ProfilesCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isInitializing) return; // ignore events during startup

            if (ProfilesCombo.SelectedItem is Profile p)
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
                            GameConfigService.UpdateSingleplayerMode();
                        }
                        else
                        {
                            // Even if address is empty, we update (passing empty strings) to clear old values
                            GameConfigService.UpdateLastConnectedServer(_current.ServerAddress, _current.Password);
                        }

                        // Write mod list
                        ModListService.WriteConanModListTxt(_current.ModIds);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProfileChange] Failed to update config: {ex.Message}");
                    }
                }
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
