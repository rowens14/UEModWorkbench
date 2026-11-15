using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UAssetAPI;
using IrrModWorkbench.Models;
using IrrModWorkbench.Services;

namespace IrrModWorkbench
{
    public partial class MainWindow
    {
        private void PopulateProjectTree(string? projectFolder)
        {
            if (ProjectTree == null) return;
            ProjectTree.Items.Clear();
            if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            {
                ProjectTree.Items.Add(new TreeViewItem { Header = "No project open", IsEnabled = false });
                return;
            }
            var root = new TreeViewItem { Header = Path.GetFileName(projectFolder), IsExpanded = true, Tag = projectFolder };
            BuildProjectTreeNodes(root, projectFolder, string.Empty);
            ProjectTree.Items.Add(root);
        }

        private void BuildProjectTreeNodes(TreeViewItem parent, string folder, string filter)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d))
                {
                    var name = Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        && !Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Any(f => Path.GetFileName(f).Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var node = new TreeViewItem { Header = name, Tag = dir, IsExpanded = false };
                    BuildProjectTreeNodes(node, dir, filter);
                    if (node.Items.Count > 0 || string.IsNullOrEmpty(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        parent.Items.Add(node);
                }
                foreach (var file in Directory.GetFiles(folder).OrderBy(f => f))
                {
                    var name = Path.GetFileName(file);
                    if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    var node = new TreeViewItem { Header = name, Tag = file };
                    parent.Items.Add(node);
                }
            }
            catch { }
        }

        private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProjectTree.SelectedItem is not TreeViewItem node || node.Tag is not string path || !File.Exists(path)) return;
            if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return;
            var version = GetEngineVersion();
            if (version == null) return;
            try
            {
                var savedUsmap = SettingsService.Get("LastUsmapPath");
                if (string.IsNullOrWhiteSpace(savedUsmap) || !File.Exists(savedUsmap)) savedUsmap = null;
                var asset = AssetService.Load(path, version.Value, savedUsmap);
                OpenAssetInTab(asset, path);
                LogAssetInfo(asset);
            }
            catch (Exception ex)
            {
                VM.Log($"Open Asset failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Open Asset", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
