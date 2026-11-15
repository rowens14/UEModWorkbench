using System.ComponentModel;
using System.Runtime.CompilerServices;
using UAssetAPI;

namespace IrrModWorkbench.Models
{
    public class OpenAssetTab : INotifyPropertyChanged
    {
        public UAsset Asset { get; set; } = null!;
        public string Path { get; set; } = string.Empty;

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnPropertyChanged(); }
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set { if (_isDirty == value) return; _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string DisplayName => IsDirty ? Name + " *" : Name;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
