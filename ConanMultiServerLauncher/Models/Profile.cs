using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConanMultiServerLauncher.Models
{
    public class Profile : INotifyPropertyChanged
    {
        private bool _needsUpdate;
        private bool _isUpdating;
        public string Name { get; set; } = string.Empty;            // Display name for the profile
        public string? ServerAddress { get; set; }                   // e.g. "123.45.67.89:7777" or hostname:port
        public string? Password { get; set; }
        public List<long> ModIds { get; set; } = new();             // Steam Workshop item IDs
        public bool BattlEyeEnabled { get; set; } = false;          // If true launch ConanSandbox_BE.exe, else ConanSandbox.exe

        public bool NeedsUpdate
        {
            get => _needsUpdate;
            set
            {
                if (_needsUpdate == value) return;
                _needsUpdate = value;
                OnPropertyChanged();
            }
        }

        public bool IsUpdating
        {
            get => _isUpdating;
            set
            {
                if (_isUpdating == value) return;
                _isUpdating = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}