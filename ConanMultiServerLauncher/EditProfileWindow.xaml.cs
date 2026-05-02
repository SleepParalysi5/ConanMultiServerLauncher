using ConanMultiServerLauncher.Models;
using ConanMultiServerLauncher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.IO;
using System.Diagnostics;

namespace ConanMultiServerLauncher
{
    public partial class EditProfileWindow : Window
    {
        private readonly Profile _profile;
        private readonly ObservableCollection<ModItem> _currentMods = new();
        public bool Result { get; private set; }

        public EditProfileWindow(Profile profile)
        {
            InitializeComponent();
            _profile = profile;

            ProfileName.Text = _profile.Name;
            ServerAddress.Text = _profile.ServerAddress ?? string.Empty;
            ServerPassword.Password = _profile.Password ?? string.Empty;
            BattlEyeCheckbox.IsChecked = _profile.BattlEyeEnabled;

            foreach (var id in _profile.ModIds)
            {
                _currentMods.Add(new ModItem 
                { 
                    PublishedFileId = id, 
                    DisplayLabel = ModListService.GetDisplayLabelForId(id) 
                });
            }
            ModsList.ItemsSource = _currentMods;
            ModsCountText.Text = $"Mods: {_profile.ModIds.Count}";
            
            _ = CheckForModUpdatesAsync();
        }

        private async System.Threading.Tasks.Task<bool> CheckForModUpdatesAsync()
        {
            if (_profile.ModIds.Count == 0) return false;

            bool anyUpdate = false;
            try
            {
                var modIds = _profile.ModIds.ToList();
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

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = ProfileName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                System.Windows.MessageBox.Show("Profile Name cannot be empty.");
                return;
            }

            _profile.Name = name;
            _profile.ServerAddress = string.IsNullOrWhiteSpace(ServerAddress.Text) ? null : ServerAddress.Text.Trim();
            _profile.Password = string.IsNullOrWhiteSpace(ServerPassword.Password) ? null : ServerPassword.Password;
            _profile.BattlEyeEnabled = BattlEyeCheckbox.IsChecked == true;
            _profile.ModIds = _currentMods.Select(m => m.PublishedFileId).Distinct().ToList();

            Result = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PasteMods_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SteamModsDialog { Owner = this };
            if (System.Windows.Clipboard.ContainsText())
            {
                dialog.ModsTextBox.Text = System.Windows.Clipboard.GetText();
                dialog.ModsTextBox.SelectAll();
            }

            if (dialog.ShowDialog() != true) return;
            var text = dialog.ModsText;

            var ids = ModListService.ExtractModIdsFromAny(text);
            if (ids.Count == 0)
            {
                System.Windows.MessageBox.Show("No Workshop IDs found.");
                return;
            }
            
            MergeMods(ids);
        }

        private void MergeMods(IEnumerable<long> ids)
        {
            var set = new HashSet<long>(_currentMods.Select(m => m.PublishedFileId));
            var added = 0;
            foreach (var id in ids)
            {
                if (set.Add(id))
                {
                    _currentMods.Add(new ModItem 
                    { 
                        PublishedFileId = id, 
                        DisplayLabel = ModListService.GetDisplayLabelForId(id) 
                    });
                    added++;
                }
            }
            ModsCountText.Text = $"Mods: {_currentMods.Count} (added {added})";
            _ = CheckForModUpdatesAsync();
        }

        private void ClearMods_Click(object sender, RoutedEventArgs e)
        {
            _currentMods.Clear();
            ModsCountText.Text = "Mods: 0";
        }
    }
}
