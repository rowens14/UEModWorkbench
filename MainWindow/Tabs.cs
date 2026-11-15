using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UAssetAPI;
using IrrModWorkbench.Models;
using IrrModWorkbench.Services;

namespace IrrModWorkbench
{
    public partial class MainWindow
    {
        private void OpenAssetInTab(UAsset asset, string path)
        {
            var existing = VM.OpenTabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActivateTab(existing);
                return;
            }
            var tab = new OpenAssetTab
            {
                Asset = asset,
                Path = path,
                Name = Path.GetFileName(path),
                IsDirty = false
            };
            VM.OpenTabs.Add(tab);
            ActivateTab(tab);
        }

        private void ActivateTab(OpenAssetTab tab)
        {
            VM.ActiveTab = tab;
            VM.CurrentAsset = tab.Asset;
            VM.CurrentAssetPath = tab.Path;
            VM.IsDirty = tab.IsDirty;
            AssetService.PopulateTree(AssetTree, VM.CurrentAsset, string.Empty);
            StatusBarAsset.Text = tab.Path;
            RefreshJsonView();
            ShowJsonDetails();
            if (CurrentAssetNameText != null) CurrentAssetNameText.Text = tab.DisplayName;
            UpdateTitle();
        }

        private void CloseTab(OpenAssetTab tab)
        {
            if (tab.IsDirty)
            {
                var res = MessageBox.Show($"'{tab.Name}' has unsaved changes. Save before closing?",
                    "Close tab", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (res == MessageBoxResult.Cancel) return;
                if (res == MessageBoxResult.Yes)
                {
                    VM.CurrentAsset = tab.Asset;
                    VM.CurrentAssetPath = tab.Path;
                    VM.IsDirty = true;
                    ApplyJsonChangesToAsset(promptConfirmation: false);
                    SaveAssetTo(tab.Path);
                    tab.IsDirty = VM.IsDirty;
                }
            }
            VM.OpenTabs.Remove(tab);
            if (VM.OpenTabs.Count == 0)
            {
                VM.ActiveTab = null;
                VM.CurrentAsset = null;
                VM.CurrentAssetPath = null;
                VM.IsDirty = false;
                ShowAssetBrowser();
                UpdateTitle();
                return;
            }
            if (VM.OpenTabs.Count > 0) ActivateTab(VM.OpenTabs[VM.OpenTabs.Count - 1]);
        }

        private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is OpenAssetTab tab)
                ActivateTab(tab);
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is OpenAssetTab tab)
                CloseTab(tab);
        }
    }
}
