using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UAssetAPI;
using IrrModWorkbench.Services;

namespace IrrModWorkbench
{
    public partial class MainWindow
    {
        private void BuildMergeButton_Click(object sender, RoutedEventArgs e)
        {
            RunBuildMerge();
        }

        private void ExportModifiedButton_Click(object sender, RoutedEventArgs e)
        {
            ExportModifiedAssets();
        }

        private void ExportAndBuildButton_Click(object sender, RoutedEventArgs e)
        {
            int exported = ExportModifiedAssets();
            if (exported > 0)
            {
                VM.Log("Proceeding to build & merge...");
                RunBuildMerge();
            }
            else
            {
                VM.Log("Nothing exported; build skipped.");
            }
        }

        private int ExportModifiedAssets()
        {
            if (VM.CurrentProjectFolder == null)
            {
                VM.Log("No project open.");
                return 0;
            }
            var src = Path.Combine(VM.CurrentProjectFolder, "ModifiedAssets");
            var dst = Path.Combine(VM.CurrentProjectFolder, "Output");
            if (!Directory.Exists(src))
            {
                VM.Log("No ModifiedAssets folder.");
                return 0;
            }
            Directory.CreateDirectory(dst);
            int count = 0;
            foreach (var file in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
                count++;
            }
            VM.Log($"Exported {count} modified asset(s) to Output.");
            return count;
        }

        private void RunBuildMerge()
        {
            if (VM.CurrentProjectFolder == null)
            {
                VM.Log("No project open.");
                MessageBox.Show("Open or create a project first.", "Build & Merge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var outputFolder = Path.Combine(VM.CurrentProjectFolder, "Output");
            if (!Directory.Exists(outputFolder) || !Directory.GetFiles(outputFolder, "*.*", SearchOption.AllDirectories).Any())
            {
                VM.Log("No exported assets found in Output. Run 'Export Modified' first.");
                MessageBox.Show("Run 'Export Modified' first so there are assets to pack.", "Build & Merge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isNewItem = false;
            foreach (var file in Directory.GetFiles(outputFolder, "*.*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("ID_", StringComparison.OrdinalIgnoreCase))
                {
                    if (name.Contains("NewVariant") || System.Text.RegularExpressions.Regex.IsMatch(name, @"_\d+$"))
                    {
                        isNewItem = true;
                        break;
                    }
                }
            }
            VM.Log(isNewItem ? "New-item mod detected (merger required)." : "Direct-override mod detected (merger not required).");

            string? mergerPath = null;
            if (isNewItem)
            {
                var savedMerger = SettingsService.Get("MergerExePath");
                if (string.IsNullOrWhiteSpace(savedMerger) || !File.Exists(savedMerger))
                {
                    MessageBox.Show("Select the IRR Mod Merger executable.", "Build & Merge", MessageBoxButton.OK, MessageBoxImage.Information);
                    var mergerDialog = new OpenFileDialog
                    {
                        Title = "Select IRR Mod Merger executable",
                        Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
                    };
                    if (mergerDialog.ShowDialog() != true)
                        return;
                    savedMerger = mergerDialog.FileName;
                    SettingsService.Set("ToolsManuallyConfigured", "true");
                }
                mergerPath = savedMerger;
                if (!string.IsNullOrWhiteSpace(mergerPath)) SettingsService.Set("MergerExePath", mergerPath);
            }

            string? repakPath = ToolDiscovery.FindRepak() ?? ToolDiscovery.FindPacker();
            if (string.IsNullOrWhiteSpace(repakPath) || !File.Exists(repakPath!))
            {
                VM.Log("repak.exe not found. Cannot build.");
                MessageBox.Show("repak.exe not found. Build aborted.", "Build & Merge", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            VM.Log($"repak: {repakPath}");

            var paksFolder = ResolveGamePaksFolder();
            if (string.IsNullOrWhiteSpace(paksFolder) || !Directory.Exists(paksFolder))
            {
                VM.Log("Game Paks folder not set.");
                return;
            }

            VM.Log($"Starting build & merge. Output: {outputFolder}");
            SetBusy(true);
            var projectFolder = VM.CurrentProjectFolder;
            Task.Run(() =>
            {
                string? pakPath = null;
                try
                {
                    Dispatcher.Invoke(() => VM.Log("Packing modified assets with repak..."));
                    var tempPakPath = Path.Combine(Path.GetTempPath(), $"irrmodworkbench_{Guid.NewGuid():N}.pak");
                    var projectName = Path.GetFileName((projectFolder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(projectName)) projectName = "Mod";
                    var basePakName = $"pakchunk99-{projectName}.pak";
                    pakPath = Path.Combine(paksFolder, basePakName);
                    if (File.Exists(pakPath))
                    {
                        int i = 2;
                        string candidate;
                        do
                        {
                            candidate = Path.Combine(paksFolder, $"pakchunk99-{projectName}_{i}.pak");
                            i++;
                        } while (File.Exists(candidate));
                        pakPath = candidate;
                        Dispatcher.Invoke(() => VM.Log($"A pak named '{basePakName}' already exists; created '{Path.GetFileName(pakPath)}' instead."));
                    }
                    try
                    {
                        PackingService.RunRepak(repakPath!, outputFolder, tempPakPath, "../../../Test_C/Content/");
                    }
                    catch (Exception repakEx)
                    {
                        Dispatcher.Invoke(() => VM.Log($"repak failed: {repakEx.Message}"));
                        throw;
                    }
                    if (!File.Exists(tempPakPath))
                        throw new Exception("Pack produced no output file.");
                    File.Copy(tempPakPath, pakPath!, true);
                    try { File.Delete(tempPakPath); } catch { }
                    Dispatcher.Invoke(() => VM.Log($"Packing complete: {Path.GetFileName(pakPath)}"));

                    if (isNewItem)
                    {
                        Dispatcher.Invoke(() => VM.Log("New-item mod detected. Running IRR Mod Merger..."));
                        MergeService.RunMerger(mergerPath!, msg => Dispatcher.Invoke(() => VM.Log(msg)));
                        Dispatcher.Invoke(() =>
                        {
                            VM.Log("Build & Merge completed successfully.");
                            VM.Log("You can now launch the game to see your changes.");
                            var pakCount = Directory.GetFiles(paksFolder, "*.pak").Length;
                            VM.Log($"Game Paks folder: {paksFolder} ({pakCount} .pak file(s)).");
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = $"\"{paksFolder}\"",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception openEx)
                            {
                                VM.Log($"Could not open Paks folder: {openEx.Message}");
                            }
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            VM.Log("Direct override mod detected. Skipping merger.");
                            VM.Log("The .pak has been placed in the game Paks folder.");
                            VM.Log("You can now launch the game to see your changes.");
                            var pakCount = Directory.GetFiles(paksFolder, "*.pak").Length;
                            VM.Log($"Game Paks folder: {paksFolder} ({pakCount} .pak file(s)).");
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = $"\"{paksFolder}\"",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception openEx)
                            {
                                VM.Log($"Could not open Paks folder: {openEx.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        VM.Log($"Build & Merge failed: {ex.GetType().FullName}: {ex.Message}");
                        VM.Log(ex.StackTrace ?? string.Empty);
                        VM.Log("The game was NOT updated. Fix the error and try again.");
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() => SetBusy(false));
                }
            });
        }

        private string? ResolveGamePaksFolder()
        {
            var saved = SettingsService.Get("GamePaksFolder");
            if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                return saved;

            try
            {
                var mergerIni = Path.Combine(Path.GetDirectoryName(SettingsService.Get("MergerExePath") ?? string.Empty) ?? string.Empty, "irr-mod-merger.ini");
                if (!File.Exists(mergerIni))
                {
                    foreach (var candidate in new[] {
                        @"E:\My Modding Projects\IRR Mod Merger\irr-mod-merger.ini",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "irr-mod-merger.ini") })
                    {
                        if (File.Exists(candidate)) { mergerIni = candidate; break; }
                    }
                }
                if (File.Exists(mergerIni))
                {
                    foreach (var line in File.ReadAllLines(mergerIni))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("game_folder", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = trimmed.IndexOf('=');
                            if (idx >= 0)
                            {
                                var gameFolder = trimmed.Substring(idx + 1).Trim().Replace('/', Path.DirectorySeparatorChar);
                                var paks = Path.Combine(gameFolder, "Test_C", "Content", "Paks");
                                if (Directory.Exists(paks))
                                {
                                    SettingsService.Set("GamePaksFolder", paks);
                                    return paks;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            var initial = Directory.Exists(@"E:\My Modding Projects") ? @"E:\My Modding Projects" : null;
            var picker = new Views.FolderPickerWindow("Select your game's Paks folder", initial);
            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedFolder))
            {
                SettingsService.Set("GamePaksFolder", picker.SelectedFolder);
                return picker.SelectedFolder;
            }
            return null;
        }
    }
}
