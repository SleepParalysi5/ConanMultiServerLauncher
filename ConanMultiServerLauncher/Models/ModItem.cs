using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConanMultiServerLauncher.Models
{
    public class ModItem : INotifyPropertyChanged
    {
        private bool _needsUpdate;
        private string _displayLabel = string.Empty;

        public long PublishedFileId { get; set; }

        public string DisplayLabel
        {
            get => _displayLabel;
            set
            {
                _displayLabel = value;
                OnPropertyChanged();
            }
        }

        public bool NeedsUpdate
        {
            get => _needsUpdate;
            set
            {
                _needsUpdate = value;
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
