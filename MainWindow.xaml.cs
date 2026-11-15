using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using ICSharpCode.AvalonEdit.Highlighting;
using IrrModWorkbench.Models;
using IrrModWorkbench.Services;
using IrrModWorkbench.ViewModels;

namespace IrrModWorkbench
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM => (MainViewModel)DataContext;



        private UAssetAPI.PropertyTypes.Objects.PropertyData? _selectedProperty;



        private readonly IrrModWorkbench.Services.BracketMatchTransformer _bracketTransformer = new();

        private static readonly string[] CommonAttributeKeywords = new[]
        {
            "health", "maxhealth", "hp", "durability", "damage", "armor", "defence", "defense",
            "stamina", "speed", "weight", "mass", "cost", "price", "value", "amount", "count",
            "radius", "range", "force", "power", "level", "capacity", "volume", "heal", "regen",
            "cooldown", "delay", "duration", "chance", "multiplier", "bonus", "penalty",
            "threshold", "limit", "max", "min"
        };        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            vm.LogService = new DelegateLogService(Log);
            vm.ClearTerminal = () => TerminalBox.Clear();
            vm.NewProject = () => NewProjectButton_Click(null!, null!);
            vm.OpenProject = () => OpenProjectButton_Click(null!, null!);
            vm.CloseProject = () => CloseProjectButton_Click(null!, null!);
            vm.OpenAsset = () => OpenAssetButton_Click(null!, null!);
            vm.SetUsmap = () => SetUsmapButton_Click(null!, null!);
            vm.ApplyJson = () => ApplyJsonButton_Click(null!, null!);
            vm.SaveAsset = () => SaveAssetButton_Click(null!, null!);
            vm.ExportAndBuild = () => ExportAndBuildButton_Click(null!, null!);
            vm.RolloverMods = () => RolloverModsButton_Click(null!, null!);
            DataContext = vm;
            ThemeManager.ApplyTheme("Base16 Light");

            // Register custom JSON highlighting (VS Code-like).
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("IrrModWorkbench.Resources.JsonHighlighting.xshd");
                if (stream != null)
                {
                    using var reader = new System.Xml.XmlTextReader(stream);
                    var def = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(reader, ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
                    ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.RegisterHighlighting("JSON", new[] { ".json" }, def);
                }
            }
            catch { }

            if (JsonEditor != null)
            {
                JsonEditor.TextArea.TextView.LineTransformers.Add(_bracketTransformer);
                JsonEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            }
            AssetList.MouseDoubleClick += AssetList_MouseDoubleClick;
            AssetList.KeyDown += AssetList_KeyDown;
            AssetTree.SelectedItemChanged += AssetTree_SelectedItemChanged;
            AssetList.SelectionChanged += AssetList_SelectionChanged;
            if (OpenTabsControl != null) OpenTabsControl.ItemsSource = VM.OpenTabs;



            var savedSearch = SettingsService.Get("AssetBrowserSearch");
            if (!string.IsNullOrWhiteSpace(savedSearch))
                AssetSearchBoxFiles.Text = savedSearch;
            var savedFilter = SettingsService.Get("AssetBrowserTypeFilter");
            if (!string.IsNullOrWhiteSpace(savedFilter))
            {
                foreach (ComboBoxItem item in AssetTypeFilter.Items)
                {
                    if (item.Content?.ToString() == savedFilter)
                    {
                        AssetTypeFilter.SelectedItem = item;
                        break;
                    }
                }
            }

            UpdateUsmapStatusBar();
            if (CloseProjectButton != null) CloseProjectButton.IsEnabled = false;
            VM.Log("IRR Mod Workbench started.");
        }
        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var initial = SettingsService.GetValidPath("NewProjectBase");
            if (string.IsNullOrWhiteSpace(initial))
                initial = Directory.Exists(@"E:\My Modding Projects") ? @"E:\My Modding Projects" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var picker = new Views.FolderPickerWindow("Create or select a folder for the new project", initial);
            if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedFolder))
                return;

            var projectFolder = picker.SelectedFolder;
            try
            {
                ProjectService.InitializeProject(projectFolder);
                VM.CurrentProjectFolder = projectFolder;
                UpdateStatusBarProject();
                var parent = Path.GetDirectoryName(projectFolder);
                if (!string.IsNullOrWhiteSpace(parent))
                    SettingsService.Set("NewProjectBase", parent);
                VM.Log($"Created project at {VM.CurrentProjectFolder}.");
                PopulateProjectTree(VM.CurrentProjectFolder);
                RefreshAssetList(Path.Combine(VM.CurrentProjectFolder, "SourceAssets"));
                if (CloseProjectButton != null) CloseProjectButton.IsEnabled = true;
                // Immediately offer to import assets (FModel workflow).
                ImportAssetButton_Click(null!, null!);
            }
            catch (Exception ex)
            {
                VM.Log($"New Project failed: {ex.Message}");
                MessageBox.Show(ex.Message, "New Project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RolloverProject()
        {
            if (VM.CurrentProjectFolder == null) return;

            var picker = new Views.FolderPickerWindow("Select new base assets folder (unpacked game)", null);
            if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedFolder))
                return;
            var newBaseFolder = picker.SelectedFolder;

            var version = GetEngineVersion();
            if (version == null) return;
            var usmap = SettingsService.GetValidPath("LastUsmapPath");
            if (string.IsNullOrWhiteSpace(usmap) || !File.Exists(usmap)) usmap = null;

            // Backup project.
            var backupFolder = VM.CurrentProjectFolder + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            try { CopyDirectory(VM.CurrentProjectFolder, backupFolder); }
            catch (Exception ex) { VM.Log($"Backup failed: {ex.Message}"); return; }
            VM.Log($"Project backed up to {backupFolder}.");

            var oldSource = Path.Combine(VM.CurrentProjectFolder, "SourceAssets");
            var modifiedRoot = Path.Combine(VM.CurrentProjectFolder, "ModifiedAssets");
            var newSource = Path.Combine(VM.CurrentProjectFolder, "SourceAssets_new");

            try { if (Directory.Exists(newSource)) Directory.Delete(newSource, true); } catch { }
            Directory.CreateDirectory(newSource);
            ImportService.ImportAssetFolderWithResult(newBaseFolder, newSource);
            VM.Log("Imported new base assets.");

            int updated = 0, skipped = 0, failed = 0;

            // Build a lookup of new-base files by file name for flexible matching.
            var newBaseFiles = Directory.Exists(newSource)
                ? Directory.GetFiles(newSource, "*.uasset", SearchOption.AllDirectories).ToList()
                : new List<string>();
            var newBaseByName = newBaseFiles
                .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            VM.Log($"New base contains {newBaseFiles.Count} .uasset file(s).");

            if (Directory.Exists(modifiedRoot))
            {
                foreach (var modFile in Directory.GetFiles(modifiedRoot, "*.uasset", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(modifiedRoot, modFile);
                    var oldBaseFile = Path.Combine(oldSource, rel);

                    // Flexible match: find new base by file name.
                    string? newBaseFile = null;
                    var fileName = Path.GetFileName(modFile);
                    if (newBaseByName.TryGetValue(fileName, out var candidates))
                    {
                        if (candidates.Count == 1)
                            newBaseFile = candidates[0];
                        else
                        {
                            // Prefer the candidate whose relative path best matches the old relative path.
                            newBaseFile = candidates
                                .OrderByDescending(c => Path.GetRelativePath(newSource, c).Equals(rel, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                                .ThenBy(c => c.Length)
                                .FirstOrDefault();
                        }
                    }

                    if (!File.Exists(oldBaseFile) || newBaseFile == null || !File.Exists(newBaseFile))
                    {
                        skipped++;
                        VM.Log($"Skipped (missing base): {rel}");
                        continue;
                    }

                    try
                    {
                        var oldAsset = AssetService.Load(oldBaseFile, version.Value, usmap);
                        var modAsset = AssetService.Load(modFile, version.Value, usmap);
                        var newAsset = AssetService.Load(newBaseFile, version.Value, usmap);

                        var oldJson = Newtonsoft.Json.Linq.JObject.Parse(oldAsset.SerializeJson(Newtonsoft.Json.Formatting.None));
                        var modJson = Newtonsoft.Json.Linq.JObject.Parse(modAsset.SerializeJson(Newtonsoft.Json.Formatting.None));
                        var patchJson = (Newtonsoft.Json.Linq.JObject)modJson.DeepClone();
                        RemoveUnchanged(patchJson, oldJson);

                        var changed = ApplyJsonToAsset(newAsset, patchJson, false, null, null);
                        if (changed > 0)
                        {
                            var dest = AssetPersistenceService.Save(newAsset, modFile, VM.CurrentProjectFolder);
                            updated++;
                            VM.Log($"Rolled over: {rel}");
                        }
                        else
                        {
                            skipped++;
                            VM.Log($"No changes to apply: {rel}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        VM.Log($"Rollover failed for {rel}: {ex.Message}");
                    }
                }
            }

            // Swap source folders.
            try
            {
                if (Directory.Exists(oldSource)) Directory.Delete(oldSource, true);
                Directory.Move(newSource, oldSource);
            }
            catch (Exception ex)
            {
                VM.Log($"Failed to swap source folders: {ex.Message}");
            }

            VM.Log($"Rollover complete: {updated} updated, {skipped} skipped, {failed} failed.");
            RefreshAssetList(Path.Combine(VM.CurrentProjectFolder, "SourceAssets"));
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var dest = Path.Combine(destination, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
        }

        private static void RemoveUnchanged(Newtonsoft.Json.Linq.JObject patch, Newtonsoft.Json.Linq.JObject original)
        {
            var exportsPatch = patch["Exports"] as Newtonsoft.Json.Linq.JArray;
            var exportsOrig = original["Exports"] as Newtonsoft.Json.Linq.JArray;
            if (exportsPatch == null || exportsOrig == null) return;

            foreach (var pe in exportsPatch.OfType<Newtonsoft.Json.Linq.JObject>().ToList())
            {
                var oe = exportsOrig.OfType<Newtonsoft.Json.Linq.JObject>()
                    .FirstOrDefault(o => string.Equals(o["ObjectName"]?.ToString(), pe["ObjectName"]?.ToString(), StringComparison.Ordinal));
                if (oe == null) continue;

                var dataPatch = pe["Data"] as Newtonsoft.Json.Linq.JArray;
                var dataOrig = oe["Data"] as Newtonsoft.Json.Linq.JArray;
                if (dataPatch == null || dataOrig == null) continue;

                foreach (var jp in dataPatch.OfType<Newtonsoft.Json.Linq.JObject>().ToList())
                {
                    var jpName = jp["Name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(jpName)) continue;
                    var jo = dataOrig.OfType<Newtonsoft.Json.Linq.JObject>()
                        .FirstOrDefault(o => string.Equals(o["Name"]?.ToString(), jpName, StringComparison.Ordinal));
                    if (jo == null) continue;

                    // Struct: recurse
                    if (jp["Value"] is Newtonsoft.Json.Linq.JArray && jo["Value"] is Newtonsoft.Json.Linq.JArray)
                    {
                        // If struct has StructType, treat as struct; else array.
                        if (jp["StructType"] != null)
                        {
                            RemoveUnchangedStruct(jp, jo);
                        }
                    }
                    else
                    {
                        // Primitive: remove if equal
                        var pv = jp["Value"]?.ToString();
                        var ov = jo["Value"]?.ToString();
                        if (pv == ov)
                            dataPatch.Remove(jp);
                    }
                }
            }
        }

        private static void RemoveUnchangedStruct(Newtonsoft.Json.Linq.JObject patchStruct, Newtonsoft.Json.Linq.JObject originalStruct)
        {
            var pv = patchStruct["Value"] as Newtonsoft.Json.Linq.JArray;
            var ov = originalStruct["Value"] as Newtonsoft.Json.Linq.JArray;
            if (pv == null || ov == null) return;

            foreach (var jp in pv.OfType<Newtonsoft.Json.Linq.JObject>().ToList())
            {
                var jpName = jp["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(jpName)) continue;
                var jo = ov.OfType<Newtonsoft.Json.Linq.JObject>()
                    .FirstOrDefault(o => string.Equals(o["Name"]?.ToString(), jpName, StringComparison.Ordinal));
                if (jo == null) continue;

                if (jp["Value"] is Newtonsoft.Json.Linq.JArray && jo["Value"] is Newtonsoft.Json.Linq.JArray && jp["StructType"] != null)
                {
                    RemoveUnchangedStruct(jp, jo);
                    // If no children left, remove parent
                    if ((jp["Value"] as Newtonsoft.Json.Linq.JArray)?.Count == 0)
                        pv.Remove(jp);
                }
                else
                {
                    var pvs = jp["Value"]?.ToString();
                    var ovs = jo["Value"]?.ToString();
                    if (pvs == ovs)
                        pv.Remove(jp);
                }
            }
        }

        private void CloseProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (VM.CurrentProjectFolder == null)
            {
                VM.Log("No project open.");
                return;
            }

            if (VM.IsDirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Close project anyway?",
                    "Close Project", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            VM.OpenTabs.Clear();
            VM.ActiveTab = null;

            VM.CurrentAsset = null;
            VM.CurrentAssetPath = null;
            VM.IsDirty = false;

            VM.CurrentProjectFolder = null;

            PopulateProjectTree(null);
            VM.BrowserItems.Clear();
            VM.FilteredBrowserItems.Clear();
            AssetTree.Items.Clear();
            if (JsonEditor != null) JsonEditor.Text = string.Empty;
            ShowAssetBrowser();

            UpdateStatusBarProject();
            StatusBarAsset.Text = "No asset loaded";
            if (StatusBarCounts != null) StatusBarCounts.Text = string.Empty;
            if (CloseProjectButton != null) CloseProjectButton.IsEnabled = false;
            UpdateTitle();

            VM.Log("Project closed.");
        }

        private void OpenProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var initial = SettingsService.GetValidPath("OpenProjectLastFolder");
            if (string.IsNullOrWhiteSpace(initial))
                initial = Directory.Exists(@"E:\My Modding Projects") ? @"E:\My Modding Projects" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var picker = new Views.FolderPickerWindow("Open project folder", initial);
            if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedFolder))
                return;

            VM.CurrentProjectFolder = picker.SelectedFolder;
            SettingsService.Set("OpenProjectLastFolder", VM.CurrentProjectFolder);
            UpdateStatusBarProject();
            VM.Log($"Opened project {VM.CurrentProjectFolder}.");
            PopulateProjectTree(VM.CurrentProjectFolder);
            RefreshAssetList(Path.Combine(VM.CurrentProjectFolder, "SourceAssets"));
            if (CloseProjectButton != null) CloseProjectButton.IsEnabled = true;

            var meta = ProjectMetadataService.Load(VM.CurrentProjectFolder);
            var currentUsmap = SettingsService.GetValidPath("LastUsmapPath");
            if (!string.IsNullOrWhiteSpace(meta.UsmapPath) &&
                !string.IsNullOrWhiteSpace(currentUsmap) &&
                !string.Equals(meta.UsmapPath, currentUsmap, StringComparison.OrdinalIgnoreCase))
            {
                var res = MessageBox.Show(
                    "Usmap changed since this project was last opened. Game update detected. Roll over this project's mods?",
                    "Rollover mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                    RolloverProject();
            }
            meta.UsmapPath = currentUsmap;
            if (!string.IsNullOrWhiteSpace(currentUsmap) && File.Exists(currentUsmap))
                meta.UsmapLastModified = File.GetLastWriteTimeUtc(currentUsmap);
            ProjectMetadataService.Save(VM.CurrentProjectFolder, meta);
        }

        private async void ImportAssetButton_Click(object sender, RoutedEventArgs e)
        {
            var initial = SettingsService.GetValidPath("AssetImportFolder");
            if (string.IsNullOrWhiteSpace(initial))
                initial = Directory.Exists(@"E:\My Modding Projects") ? @"E:\My Modding Projects" : null;

            // Ask folder or files
            var modeResult = MessageBox.Show(
                "Import a whole folder? (Yes = folder, No = pick individual files)",
                "Import Asset",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (modeResult == MessageBoxResult.Cancel) return;

            string[] selectedFiles;
            string? sourceFolder = null;
            if (modeResult == MessageBoxResult.Yes)
            {
                var sourcePicker = new Views.FolderPickerWindow("Select folder containing assets", initial);
                if (sourcePicker.ShowDialog() != true || string.IsNullOrWhiteSpace(sourcePicker.SelectedFolder))
                    return;
                sourceFolder = sourcePicker.SelectedFolder;
                SettingsService.Set("AssetImportFolder", sourceFolder);
                selectedFiles = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);
            }
            else
            {
                var fileDialog = new OpenFileDialog
                {
                    Title = "Select asset files",
                    Filter = "All files (*.*)|*.*",
                    Multiselect = true
                };
                if (fileDialog.ShowDialog() != true || fileDialog.FileNames.Length == 0)
                    return;
                selectedFiles = fileDialog.FileNames;
            }

            // Destination: project SourceAssets if project open, otherwise prompt once
            string destinationFolder;
            if (VM.CurrentProjectFolder != null)
            {
                destinationFolder = Path.Combine(VM.CurrentProjectFolder, "SourceAssets");
            }
            else
            {
                var destPicker = new Views.FolderPickerWindow("Select destination folder", null);
                if (destPicker.ShowDialog() != true || string.IsNullOrWhiteSpace(destPicker.SelectedFolder))
                    return;
                destinationFolder = destPicker.SelectedFolder;
            }

            try
            {
                ImportProgress.Visibility = Visibility.Visible;
                SetBusy(true);
                ImportResult result;
                if (sourceFolder != null)
                    result = await Task.Run(() => ImportService.ImportAssetFolderWithResult(sourceFolder, destinationFolder));
                else
                    result = await Task.Run(() => ImportService.ImportAssetFilesWithResult(selectedFiles, destinationFolder));
                var importedFiles = Directory.GetFiles(destinationFolder, "*.*", SearchOption.AllDirectories);
                var byExtension = importedFiles
                    .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}: {g.Count()}");
                VM.Log($"Imported {result.ImportedCount} file(s) into {destinationFolder}.");
                if (result.SkippedCount > 0)
                {
                    VM.Log($"Skipped {result.SkippedCount} locked file(s):");
                    foreach (var s in result.SkippedFiles) VM.Log($"  Skipped (locked): {s}");
                }
                VM.Log("Imported extensions: " + string.Join(", ", byExtension));
                VM.Log($"Destination contains {importedFiles.Count(f => f.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))} .uasset file(s).");
                RefreshAssetList(destinationFolder);
            }
            catch (Exception ex)
            {
                VM.Log($"Import FModel failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Import FModel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportProgress.Visibility = Visibility.Collapsed;
                SetBusy(false);
            }
        }

        private void RolloverModsButton_Click(object sender, RoutedEventArgs e)
        {
            if (VM.CurrentProjectFolder == null)
            {
                MessageBox.Show("Open a project first.", "Rollover mods", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RolloverProject();
        }

        private void SetUsmapButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = PromptForUsmapPath();
            if (string.IsNullOrWhiteSpace(selected))
                return;

            var previous = VM.CurrentProjectFolder != null
                ? ProjectMetadataService.Load(VM.CurrentProjectFolder).UsmapPath
                : SettingsService.Get("LastUsmapPath");

            SettingsService.Set("LastUsmapPath", selected);
            UpdateUsmapStatusBar();
            VM.Log($"Active usmap set to: {selected}");

            if (VM.CurrentProjectFolder != null &&
                !string.IsNullOrWhiteSpace(previous) &&
                !string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase))
            {
                var res = MessageBox.Show(
                    "New usmap detected. This may indicate a game update. Roll over this project's mods?",
                    "Rollover mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                    RolloverProject();
            }

            if (VM.CurrentProjectFolder != null)
            {
                var meta = ProjectMetadataService.Load(VM.CurrentProjectFolder);
                meta.UsmapPath = selected;
                if (File.Exists(selected)) meta.UsmapLastModified = File.GetLastWriteTimeUtc(selected);
                ProjectMetadataService.Save(VM.CurrentProjectFolder, meta);
            }
        }

        private string? PromptForUsmapPath()
        {
            var current = SettingsService.GetValidPath("LastUsmapPath");
            string? initialDir = null;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                initialDir = Path.GetDirectoryName(current);
            else if (Directory.Exists(@"E:\My Modding Projects"))
                initialDir = @"E:\My Modding Projects";

            var dialog = new OpenFileDialog
            {
                Title = "Select .usmap mapping file",
                Filter = "Usmap files (*.usmap)|*.usmap|All files (*.*)|*.*"
            };
            if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                dialog.InitialDirectory = initialDir;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
                dialog.FileName = Path.GetFileName(current);

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void UpdateUsmapStatusBar()
        {
            if (StatusBarUsmap == null) return;
            var current = SettingsService.GetValidPath("LastUsmapPath");
            StatusBarUsmap.Text = string.IsNullOrWhiteSpace(current)
                ? "No usmap selected"
                : $"Usmap: {Path.GetFileName(current)}";
        }

        private void OpenAssetButton_Click(object sender, RoutedEventArgs e)
        {
            var savedAssetFolder = SettingsService.GetValidPath("AssetOpenFolder");
            if (string.IsNullOrWhiteSpace(savedAssetFolder))
                savedAssetFolder = Directory.Exists(@"E:\My Modding Projects") ? @"E:\My Modding Projects" : null;
            var dialog = new OpenFileDialog
            {
                Title = "Open Unreal asset",
                Filter = "All files (*.*)|*.*|Unreal assets (*.uasset;*.umap)|*.uasset;*.umap"
            };
            if (!string.IsNullOrWhiteSpace(savedAssetFolder) && Directory.Exists(savedAssetFolder))
                dialog.InitialDirectory = savedAssetFolder;
            if (dialog.ShowDialog() != true)
                return;
            SettingsService.Set("AssetOpenFolder", Path.GetDirectoryName(dialog.FileName) ?? string.Empty);

            var version = GetEngineVersion();
            if (version == null)
                return;

            var savedUsmap = SettingsService.GetValidPath("LastUsmapPath");
            string? usmapPath;
            if (!string.IsNullOrWhiteSpace(savedUsmap) && File.Exists(savedUsmap))
            {
                usmapPath = savedUsmap;
            }
            else
            {
                usmapPath = PromptForUsmapPath();
                if (string.IsNullOrWhiteSpace(usmapPath))
                    usmapPath = SettingsService.Get("LastUsmapPath");
                else
                    SettingsService.Set("LastUsmapPath", usmapPath);
            }

            try
            {
                var loadedAsset = AssetService.Load(dialog.FileName, version.Value, usmapPath);
                LogAssetPathStatus(loadedAsset, dialog.FileName);
                OpenAssetInTab(loadedAsset, dialog.FileName);
                LogAssetInfo(loadedAsset);
                VM.Log($"Opened asset {VM.CurrentAssetPath}.");
            }
            catch (Exception ex)
            {
                VM.Log($"Open Asset failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Open Asset", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AssetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AssetList.SelectedItem is not AssetBrowserItem item || VM.CurrentProjectFolder == null)
                return;

            var relative = item.RelativePath;
            var fullPath = Path.Combine(VM.CurrentProjectFolder, "SourceAssets", relative);
            if (!File.Exists(fullPath))
                return;

            var version = GetEngineVersion();
            if (version == null) return;

            try
            {
                var savedUsmap = SettingsService.Get("LastUsmapPath");
                if (string.IsNullOrWhiteSpace(savedUsmap) || !File.Exists(savedUsmap))
                    savedUsmap = null;
                var loadedAsset2 = AssetService.Load(fullPath, version.Value, savedUsmap);
                StatusBarAsset.Text = fullPath;
                OpenAssetInTab(loadedAsset2, fullPath);
                LogAssetInfo(loadedAsset2);
                if (IsRawExportOnly(loadedAsset2))
                {
                    var backupPath = fullPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        var restore = MessageBox.Show(
                            "This asset has no editable properties (likely corrupted by a previous save). Restore from backup?",
                            "Restore from backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (restore == MessageBoxResult.Yes)
                        {
                            try
                            {
                                var restored = AssetService.Load(backupPath, version.Value, savedUsmap);
                                OpenAssetInTab(restored, fullPath);
                                VM.Log("Restored asset from backup (in memory). Save to persist.");
                            }
                            catch (Exception ex) { VM.Log($"Restore failed: {ex.Message}"); }
                        }
                    }
                    else
                    {
                        MessageBox.Show("This asset has no editable properties and no backup was found. Re-import from FModel.",
                            "No backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                VM.Log($"Opened asset {fullPath}.");
            }
            catch (Exception ex)
            {
                VM.Log($"Open Asset failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Open Asset", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private EngineVersion? GetEngineVersion()
        {
            var saved = SettingsService.Get("LastEngineVersion");
            if (!string.IsNullOrWhiteSpace(saved) && Enum.TryParse<EngineVersion>(saved, out var v))
                return v;
            var picked = PromptForEngineVersion(EngineVersion.VER_UE5_3);
            if (picked.HasValue)
                SettingsService.Set("LastEngineVersion", picked.Value.ToString());
            return picked;
        }

        private EngineVersion? PromptForEngineVersion(EngineVersion defaultVersion = EngineVersion.VER_UE5_3)
        {
            var window = new Window
            {
                Title = "Select Unreal Engine version",
                Width = 320,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock { Text = "Engine version:", Margin = new Thickness(0, 0, 0, 8) });

            var combo = new ComboBox { ItemsSource = Enum.GetValues(typeof(EngineVersion)), SelectedItem = defaultVersion };
            stack.Children.Add(combo);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(buttonPanel);

            window.Content = stack;
            okButton.Click += (_, _) => window.DialogResult = true;
            cancelButton.Click += (_, _) => window.DialogResult = false;

            if (window.ShowDialog() == true && combo.SelectedItem is EngineVersion version)
                return version;
            return null;
        }

        private void AssetTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not TreeViewItem node)
                return;

            

            var rows = new List<PropertyRow>();

            if (node.Tag is UAssetAPI.PropertyTypes.Objects.PropertyData prop)
            {
                _selectedProperty = prop;
                // ApplyButton and CurrentValueText were removed with the inspector panel.
                rows.Add(new PropertyRow { Name = "Name", Type = "string", Value = prop.Name?.ToString() ?? string.Empty });
                rows.Add(new PropertyRow { Name = "Type", Type = "string", Value = prop.PropertyType?.ToString() ?? string.Empty });
                rows.Add(new PropertyRow { Name = "Raw Value", Type = prop.RawValue?.GetType().Name ?? "null", Value = prop.RawValue?.ToString() ?? string.Empty });
                rows.Add(new PropertyRow { Name = "Array Index", Type = "int", Value = prop.ArrayIndex.ToString() });
                rows.Add(new PropertyRow { Name = "Property Tag Flags", Type = "flags", Value = prop.PropertyTagFlags.ToString() });
                if (prop.PropertyGuid.HasValue)
                    rows.Add(new PropertyRow { Name = "Property GUID", Type = "guid", Value = prop.PropertyGuid.Value.ToString() });

                // Flatten one level of children for collection/struct properties
                if (prop is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arr && arr.Value != null)
                {
                    for (int i = 0; i < arr.Value.Length; i++)
                        rows.Add(new PropertyRow { Name = $"[{i}]", Type = arr.Value[i].PropertyType?.ToString() ?? string.Empty, Value = arr.Value[i].RawValue?.ToString() ?? string.Empty });
                }
                else if (prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData st && st.Value != null)
                {
                    foreach (var child in st.Value)
                        rows.Add(new PropertyRow { Name = child.Name?.ToString() ?? string.Empty, Type = child.PropertyType?.ToString() ?? string.Empty, Value = child.RawValue?.ToString() ?? string.Empty });
                }
            }
            else if (node.Tag is UAssetAPI.ExportTypes.Export export)
            {
                rows.Add(new PropertyRow { Name = "Export", Type = "string", Value = export.ObjectName?.ToString() ?? string.Empty });
                rows.Add(new PropertyRow { Name = "Class", Type = "string", Value = export.GetType().Name });
                rows.Add(new PropertyRow { Name = "Serial Size", Type = "int", Value = export.SerialSize.ToString() });
                rows.Add(new PropertyRow { Name = "Serial Offset", Type = "int", Value = export.SerialOffset.ToString() });
            }
            else
            {
                rows.Add(new PropertyRow { Name = "Info", Type = "string", Value = node.Header?.ToString() ?? string.Empty });
            }

            // Details are shown as JSON.

            if (node.Tag is not UAssetAPI.PropertyTypes.Objects.PropertyData)
            {
                _selectedProperty = null;
                // ApplyButton and CurrentValueText were removed with the inspector panel.
            }

            // Sync JSON editor to the selected node.
            ScrollJsonToSelectedNode(node);

          
        }

        private static object? ParsePropertyValue(UAssetAPI.PropertyTypes.Objects.PropertyData prop, string text)
        {
            var targetType = prop.RawValue?.GetType();
            if (targetType == null || targetType.IsArray || targetType.IsGenericType)
            {
                targetType = prop.PropertyType.ToString() switch
                {
                    "IntProperty" => typeof(int),
                    "FloatProperty" => typeof(float),
                    "DoubleProperty" => typeof(double),
                    "BoolProperty" => typeof(bool),
                    "ByteProperty" => typeof(byte),
                    "Int64Property" => typeof(long),
                    "ObjectProperty" => typeof(UAssetAPI.UnrealTypes.FPackageIndex),
                    _ => typeof(string)
                };
            }

            if (targetType == typeof(bool))
                return bool.Parse(text.Trim());
            if (targetType == typeof(int))
                return int.Parse(text.Trim());
            if (targetType == typeof(float))
                return float.Parse(text.Trim());
            if (targetType == typeof(double))
                return double.Parse(text.Trim());
            if (targetType == typeof(byte))
                return byte.Parse(text.Trim());
            if (targetType == typeof(long))
                return long.Parse(text.Trim());

            // Complex Unreal types: only FPackageIndex is supported (as an int index).
            if (targetType == typeof(UAssetAPI.UnrealTypes.FPackageIndex))
                return new UAssetAPI.UnrealTypes.FPackageIndex(int.Parse(text.Trim()));

            // Anything else is not safely editable as text.
            throw new NotSupportedException($"Property type '{targetType?.Name ?? prop.PropertyType?.ToString()}' is not editable as text.");
        }

        private bool IsRawExportOnly(UAssetAPI.UAsset asset)
        {
            if (asset == null) return false;
            try
            {
                foreach (var ex in asset.Exports)
                {
                    if (ex is UAssetAPI.ExportTypes.NormalExport ne && ne.Data != null && ne.Data.Count > 0)
                        return false;
                }
                return asset.Exports.Count > 0;
            }
            catch { return false; }
        }

        private void LogAssetPathStatus(UAssetAPI.UAsset asset, string filePath)
        {
            if (asset == null) return;
            try
            {
                var derived = GetAssetRelativeGamePath(asset, Path.GetExtension(filePath));
                string onDiskRel = filePath;
                if (VM.CurrentProjectFolder != null)
                {
                    var srcRoot = Path.Combine(VM.CurrentProjectFolder, "SourceAssets");
                    if (filePath.StartsWith(srcRoot, StringComparison.OrdinalIgnoreCase))
                        onDiskRel = Path.GetRelativePath(srcRoot, filePath);
                }
                if (!string.IsNullOrWhiteSpace(derived))
                {
                    VM.Log($"Asset game path: {derived}");
                    if (!string.Equals(derived, onDiskRel, StringComparison.OrdinalIgnoreCase))
                        VM.Log($"WARNING: on-disk relative path '{onDiskRel}' differs from game path '{derived}'. Save will use the game path.");
                }
            }
            catch (Exception ex)
            {
                VM.Log($"Path status failed: {ex.Message}");
            }
        }

        private void LogAssetInfo(UAssetAPI.UAsset asset)
        {
            if (asset == null) return;
            try
            {
                VM.Log($"Asset exports: {asset.Exports.Count}");
                for (int i = 0; i < asset.Exports.Count; i++)
                {
                    var ex = asset.Exports[i];
                    int dataCount = 0;
                    if (ex is UAssetAPI.ExportTypes.NormalExport ne && ne.Data != null)
                        dataCount = ne.Data.Count;
                    VM.Log($"  [{i}] {ex.ObjectName} ({ex.GetType().Name}) - {dataCount} prop(s)");
                }
            }
            catch (Exception ex)
            {
                VM.Log($"LogAssetInfo failed: {ex.Message}");
            }
        }

        private void UpdateTitle()
        {
            Title = VM.IsDirty ? "UE Asset Workbench *" : "UE Asset Workbench";
        }

        private void AssetList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && AssetList.SelectedItem is AssetBrowserItem item && VM.CurrentProjectFolder != null)
            {
                AssetList_MouseDoubleClick(sender, null!);
                e.Handled = true;
            }
        }

        private void ShowAssetBrowser()
        {
            if (AssetBrowserView != null) AssetBrowserView.Visibility = Visibility.Visible;
            if (JsonDetailsView != null) JsonDetailsView.Visibility = Visibility.Collapsed;
        }

        private void ShowJsonDetails()
        {
            if (AssetBrowserView != null) AssetBrowserView.Visibility = Visibility.Collapsed;
            if (JsonDetailsView != null) JsonDetailsView.Visibility = Visibility.Visible;
        }

        private void BackToBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAssetBrowser();
        }

        private void ApplyJsonButton_Click(object sender, RoutedEventArgs e)
        {
            if (VM.CurrentAsset == null)
            {
                VM.Log("No asset loaded to apply.");
                return;
            }
            if (ApplyJsonChangesToAsset(promptConfirmation: false))
            {
                VM.Log("JSON changes applied to in-memory asset. Click Save Asset to persist.");
                AssetService.PopulateTree(AssetTree, VM.CurrentAsset, string.Empty);
            }
        }

        private void SaveAssetButton_Click(object sender, RoutedEventArgs e)
        {
            if (VM.CurrentAsset == null)
            {
                VM.Log("No asset loaded to save.");
                return;
            }

            // Apply any JSON edits first (with confirmation), then save to disk.
            if (!ApplyJsonChangesToAsset(promptConfirmation: true))
                return;

            if (string.IsNullOrWhiteSpace(VM.CurrentAssetPath))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save asset as",
                    Filter = "Unreal assets (*.uasset)|*.uasset|All files (*.*)|*.*",
                    FileName = "asset.uasset"
                };
                if (dlg.ShowDialog() != true) return;
                VM.CurrentAssetPath = dlg.FileName;
            }

            SaveAssetTo(VM.CurrentAssetPath);
        }

        private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            if (VM.CurrentAsset == null)
            {
                VM.Log("No asset loaded to save.");
                return;
            }

            if (!ApplyJsonChangesToAsset(promptConfirmation: true))
                return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save asset as",
                Filter = "Unreal assets (*.uasset)|*.uasset|All files (*.*)|*.*",
                FileName = Path.GetFileName(VM.CurrentAssetPath ?? "asset.uasset")
            };
            if (dialog.ShowDialog() != true)
                return;

            SaveAssetTo(dialog.FileName);
        }

        private void RepairEnums(UAssetAPI.UAsset asset)
        {
            if (asset == null) return;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.NormalExport ne && ne.Data != null)
                    foreach (var p in ne.Data)
                        RepairProperty(p, asset);
            }
        }

        private void NullAllEnums(UAssetAPI.UAsset asset)
        {
            if (asset == null) return;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.NormalExport ne && ne.Data != null)
                    foreach (var p in ne.Data)
                        NullEnumProperty(p);
            }
        }

        private void NullEnumProperty(UAssetAPI.PropertyTypes.Objects.PropertyData prop)
        {
            if (prop == null) return;
            if (prop is UAssetAPI.PropertyTypes.Objects.EnumPropertyData epd)
            {
                try { epd.Value = null; } catch { }
                try { epd.RawValue = null; } catch { }
            }
            if (prop is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arr && arr.Value != null)
                foreach (var c in arr.Value) NullEnumProperty(c);
            if (prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData st && st.Value != null)
                foreach (var c in st.Value) NullEnumProperty(c);
            if (prop is UAssetAPI.PropertyTypes.Objects.MapPropertyData mp && mp.Value != null)
                foreach (var kv in mp.Value) { NullEnumProperty(kv.Key); NullEnumProperty(kv.Value); }
            if (prop is UAssetAPI.PropertyTypes.Objects.SetPropertyData sp && sp.Value != null)
                foreach (var c in sp.Value) NullEnumProperty(c);
        }

        private void RepairProperty(UAssetAPI.PropertyTypes.Objects.PropertyData prop, UAssetAPI.UAsset asset)
        {
            if (prop == null) return;
            if (prop is UAssetAPI.PropertyTypes.Objects.EnumPropertyData epd)
            {
                var names = asset.GetNameMapIndexList();
                // Enum value/type/inner names are often not in the asset's main name map (they come from the
                // enum type itself), so do NOT treat "not in name map" as missing. Only repair truly null/empty.
                bool valueMissing = epd.Value == null || string.IsNullOrWhiteSpace(epd.Value?.ToString());
                bool typeMissing = epd.EnumType == null || string.IsNullOrWhiteSpace(epd.EnumType?.ToString());
                bool innerMissing = epd.InnerType == null || string.IsNullOrWhiteSpace(epd.InnerType?.ToString());

                // For ByteProperty-backed enums, UAssetAPI needs a valid byte RawValue.
                if (prop.PropertyType?.ToString() == "ByteProperty" && !(epd.RawValue is byte))
                {
                    try { epd.RawValue = (byte)0; } catch { }
                }

                if (valueMissing || typeMissing || innerMissing)
                {
                    try
                    {
                        string chosen = (names != null && names.Count > 0) ? names[0].ToString() : "None";
                        if (names != null)
                        {
                            var none = names.FirstOrDefault(n => n.ToString().Equals("None", StringComparison.OrdinalIgnoreCase));
                            if (none != null) chosen = none.ToString();
                        }
                        var fn = new UAssetAPI.UnrealTypes.FName(asset, chosen);
                        epd.Value = fn;
                        epd.EnumType = fn;
                        epd.InnerType = fn;
                    }
                    catch { }
                }
            }
            if (prop is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arr && arr.Value != null)
                foreach (var c in arr.Value) RepairProperty(c, asset);
            if (prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData st && st.Value != null)
                foreach (var c in st.Value) RepairProperty(c, asset);
            if (prop is UAssetAPI.PropertyTypes.Objects.MapPropertyData mp && mp.Value != null)
            {
                foreach (var kv in mp.Value)
                {
                    RepairProperty(kv.Key, asset);
                    RepairProperty(kv.Value, asset);
                }
            }
            if (prop is UAssetAPI.PropertyTypes.Objects.SetPropertyData sp && sp.Value != null)
            {
                foreach (var c in sp.Value) RepairProperty(c, asset);
            }
        }

        private void SaveAssetTo(string path)
        {
            if (VM.CurrentAsset == null) return;
            try
            {
                var dest = AssetPersistenceService.Save(VM.CurrentAsset, path, VM.CurrentProjectFolder);

                VM.CurrentAssetPath = dest;
                if (VM.ActiveTab != null) { VM.ActiveTab.Path = dest; VM.ActiveTab.IsDirty = false; }

                VM.IsDirty = false;
                UpdateTitle();
                VM.Log($"Saved asset to {path} (backup: {path}.bak).");
            }
            catch (Exception ex)
            {
                VM.Log($"Save failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Save Asset", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal static string ResolveSaveDestination(string projectFolder, UAsset? asset, string originalPath)
        {
            var modifiedRoot = Path.Combine(projectFolder, "ModifiedAssets");
            var srcRoot = Path.Combine(projectFolder, "SourceAssets");
            var derived = GetAssetRelativeGamePath(asset, Path.GetExtension(originalPath));
            string rel;
            if (!string.IsNullOrWhiteSpace(derived))
            {
                rel = derived!;
            }
            else if (originalPath.StartsWith(srcRoot, StringComparison.OrdinalIgnoreCase))
            {
                rel = Path.GetRelativePath(srcRoot, originalPath);
            }
            else
            {
                rel = Path.GetFileName(originalPath);
            }

            if (Path.IsPathRooted(rel))
                throw new InvalidOperationException("Resolved asset path is not relative.");
            var dest = Path.Combine(modifiedRoot, rel);
            var fullDest = Path.GetFullPath(dest);
            var fullModified = Path.GetFullPath(modifiedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullDest.StartsWith(fullModified, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved save path is outside the ModifiedAssets folder.");
            return dest;
        }

        private string? _browserFolder;

        private void RefreshAssetList(string folder)
        {
            _browserFolder = folder;
            VM.BrowserItems.Clear();
            if (!Directory.Exists(folder))
            {
                VM.BrowserItems.Clear();
                VM.FilteredBrowserItems.Clear();
                UpdateStatusBarCounts();
                return;
            }

            var modifiedRoot = VM.CurrentProjectFolder != null
                ? Path.Combine(VM.CurrentProjectFolder, "ModifiedAssets")
                : null;

            foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(folder, file);
                var info = new FileInfo(file);
                var status = "Source";
                if (modifiedRoot != null)
                {
                    var modifiedPath = Path.Combine(modifiedRoot, rel);
                    if (File.Exists(modifiedPath))
                        status = "Modified";
                }
                VM.BrowserItems.Add(new AssetBrowserItem
                {
                    Name = Path.GetFileName(file),
                    RelativePath = rel,
                    Extension = Path.GetExtension(file).ToLowerInvariant(),
                    SizeBytes = info.Length,
                    Modified = info.LastWriteTime,
                    Status = status
                });
            }

            RebuildPrefixFilter();
            ApplyBrowserFilter();
            UpdateStatusBarCounts();
        }

        private void ApplyBrowserFilter()
        {
            if (AssetList == null || AssetSearchBoxFiles == null || AssetTypeFilter == null)
                return;
            var search = AssetSearchBoxFiles?.Text?.Trim() ?? string.Empty;
            VM.AssetSearchText = search;
            var prefixFilter = (AssetTypeFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            VM.SelectedAssetPrefix = prefixFilter;

            var filtered = VM.BrowserItems.Where(item =>
            {
                if (!string.IsNullOrEmpty(search) &&
                    !item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !item.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (prefixFilter != "All")
                {
                    var itemPrefix = GetFilePrefix(item.Name);
                    if (!string.Equals(itemPrefix, prefixFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

            VM.FilteredBrowserItems.Clear();
            foreach (var item in filtered) VM.FilteredBrowserItems.Add(item);
            try { SettingsService.Set("AssetBrowserSearch", search); } catch { }
            try { SettingsService.Set("AssetBrowserTypeFilter", prefixFilter); } catch { }
        }

        private static string GetFilePrefix(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var idx = name.IndexOf('_');
            if (idx <= 0) return "(none)";
            return name.Substring(0, idx);
        }

        private void RebuildPrefixFilter()
        {
            if (AssetTypeFilter == null) return;
            var current = (AssetTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";

            var prefixes = VM.BrowserItems
                .Select(i => GetFilePrefix(i.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AssetTypeFilter.SelectionChanged -= AssetTypeFilter_SelectionChanged;
            AssetTypeFilter.Items.Clear();
            AssetTypeFilter.Items.Add(new ComboBoxItem { Content = "All", IsSelected = current == "All" });
            foreach (var p in prefixes)
            {
                AssetTypeFilter.Items.Add(new ComboBoxItem { Content = p, IsSelected = string.Equals(p, current, StringComparison.OrdinalIgnoreCase) });
            }

            // Restore selection if still present; otherwise default to All.
            var match = AssetTypeFilter.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Content?.ToString(), current, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                AssetTypeFilter.SelectedItem = match;
            else
                AssetTypeFilter.SelectedIndex = 0;

            AssetTypeFilter.SelectionChanged += AssetTypeFilter_SelectionChanged;
        }

        private void AssetFilesSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyBrowserFilter();

        private void AssetTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyBrowserFilter();

        private void AssetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AssetList.SelectedItem is AssetBrowserItem item)
                StatusBarAsset.Text = item.RelativePath;
        }

        private void UpdateStatusBarCounts()
        {
            StatusBarCounts.Text = $"{VM.BrowserItems.Count} asset(s)";
        }

        private void UpdateStatusBarProject()
        {
            StatusBarProject.Text = VM.CurrentProjectFolder ?? "No project open";
        }

        private static string PromptForText(string title, string prompt, string defaultValue)
        {
            var window = new Window
            {
                Title = title,
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });

            var textBox = new TextBox { Text = defaultValue };
            stack.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(buttonPanel);

            window.Content = stack;
            okButton.Click += (_, _) => window.DialogResult = true;
            cancelButton.Click += (_, _) => window.DialogResult = false;

            if (window.ShowDialog() == true)
                return textBox.Text;
            return string.Empty;
        }

        private void SetBusy(bool busy)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetBusy(busy));
                return;
            }
            NewProjectButton.IsEnabled = !busy;
            OpenProjectButton.IsEnabled = !busy;
            OpenAssetButton.IsEnabled = !busy;
            ExportAndBuildButton.IsEnabled = !busy;
            VM.IsBusy = busy;
        }

        public void Log(string message)
        {
            if (!TerminalBox.Dispatcher.CheckAccess())
            {
                TerminalBox.Dispatcher.Invoke(() => Log(message));
                return;
            }
            TerminalBox.AppendText(message + Environment.NewLine);
            TerminalBox.ScrollToEnd();
        }

        private void ClearTerminalButton_Click(object sender, RoutedEventArgs e)
        {
            TerminalBox.Clear();
        }

        /* removed */
    }

}
