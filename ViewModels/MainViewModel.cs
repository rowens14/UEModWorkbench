using System;
using System.Collections.ObjectModel;
using IrrModWorkbench.Models;
using IrrModWorkbench.Services;
using UAssetAPI;

namespace IrrModWorkbench.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public MainViewModel()
        {
            ClearTerminalCommand = new RelayCommand(_ => ClearTerminal?.Invoke());
            NewProjectCommand = new RelayCommand(_ => NewProject?.Invoke());
            OpenProjectCommand = new RelayCommand(_ => OpenProject?.Invoke());
            CloseProjectCommand = new RelayCommand(_ => CloseProject?.Invoke());
            OpenAssetCommand = new RelayCommand(_ => OpenAsset?.Invoke());
            SetUsmapCommand = new RelayCommand(_ => SetUsmap?.Invoke());
            ApplyJsonCommand = new RelayCommand(_ => ApplyJson?.Invoke());
            SaveAssetCommand = new RelayCommand(_ => SaveAsset?.Invoke());
            ExportAndBuildCommand = new RelayCommand(_ => ExportAndBuild?.Invoke());
            RolloverModsCommand = new RelayCommand(_ => RolloverMods?.Invoke());
        }

        private string _title = "UE Asset Workbench";
        private bool _isBusy;
        private string _statusMessage = "Ready";
        private string _assetSearchText = string.Empty;
        private string _selectedAssetPrefix = "All";

        public ILogService? LogService { get; set; }
        public void Log(string message) => LogService?.Log(message);

        public Action? ClearTerminal { get; set; }

        public ObservableCollection<OpenAssetTab> OpenTabs { get; } = new();
        private OpenAssetTab? _activeTab;
        public OpenAssetTab? ActiveTab
        {
            get => _activeTab;
            set => SetProperty(ref _activeTab, value);
        }

        private UAsset? _currentAsset;
        public UAsset? CurrentAsset
        {
            get => _currentAsset;
            set => SetProperty(ref _currentAsset, value);
        }

        private string? _currentProjectFolder;
        public string? CurrentProjectFolder
        {
            get => _currentProjectFolder;
            set => SetProperty(ref _currentProjectFolder, value);
        }

        private string? _currentAssetPath;
        public string? CurrentAssetPath
        {
            get => _currentAssetPath;
            set => SetProperty(ref _currentAssetPath, value);
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        public Action? NewProject { get; set; }
        public Action? OpenProject { get; set; }
        public Action? CloseProject { get; set; }
        public Action? OpenAsset { get; set; }
        public Action? SetUsmap { get; set; }
        public Action? ApplyJson { get; set; }
        public Action? SaveAsset { get; set; }
        public Action? ExportAndBuild { get; set; }
        public Action? RolloverMods { get; set; }

        public RelayCommand NewProjectCommand { get; }
        public RelayCommand OpenProjectCommand { get; }
        public RelayCommand CloseProjectCommand { get; }
        public RelayCommand OpenAssetCommand { get; }
        public RelayCommand SetUsmapCommand { get; }
        public RelayCommand ApplyJsonCommand { get; }
        public RelayCommand SaveAssetCommand { get; }
        public RelayCommand ExportAndBuildCommand { get; }
        public RelayCommand RolloverModsCommand { get; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<AssetBrowserItem> BrowserItems { get; } = new();
        public ObservableCollection<AssetBrowserItem> FilteredBrowserItems { get; } = new();

        public RelayCommand ClearTerminalCommand { get; }

        public string AssetSearchText
        {
            get => _assetSearchText;
            set => SetProperty(ref _assetSearchText, value);
        }

        public string SelectedAssetPrefix
        {
            get => _selectedAssetPrefix;
            set => SetProperty(ref _selectedAssetPrefix, value);
        }
    }
}