using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace IrrModWorkbench.Services
{
    public static class IrrMergerService
    {
        public static void Run(Action<string> log, string gamePaksFolder, string outputPakPath, string? repakPath, string? jaqPath)
        {
            if (string.IsNullOrWhiteSpace(gamePaksFolder) || !Directory.Exists(gamePaksFolder))
                throw new DirectoryNotFoundException("Game Paks folder not found: " + gamePaksFolder);

            var fullPaks = Path.GetFullPath(gamePaksFolder);
            if (!fullPaks.EndsWith(Path.DirectorySeparatorChar + "Content" + Path.DirectorySeparatorChar + "Paks", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Game Paks folder must end in ...\\Content\\Paks.");

            // Normalize: gamePaksFolder is .../Test_C/Content/Paks, temp sits beside Content
            var contentRoot = Path.GetFullPath(Path.Combine(gamePaksFolder, "..", "..")); // .../Test_C/Content
            var gameRoot = Path.GetFullPath(Path.Combine(contentRoot, ".."));            // .../Test_C
            var tempRoot = Path.Combine(gameRoot, "irr-mod-merger-temp");
            Directory.CreateDirectory(tempRoot);

            var unpackRoot = Path.Combine(tempRoot, "unpack");
            var dtVendorRoot = Path.Combine(tempRoot, "dt_vendor");
            try { if (Directory.Exists(unpackRoot) && IsUnderDirectory(unpackRoot, tempRoot)) Directory.Delete(unpackRoot, true); } catch { }
            try { if (Directory.Exists(dtVendorRoot) && IsUnderDirectory(dtVendorRoot, tempRoot)) Directory.Delete(dtVendorRoot, true); } catch { }
            Directory.CreateDirectory(unpackRoot);
            Directory.CreateDirectory(dtVendorRoot);

            // 1. Enumerate mod paks
            var paks = Directory.GetFiles(gamePaksFolder, "*.pak").OrderBy(f => f).ToList();
            log($"Found {paks.Count} pak(s) in {gamePaksFolder}");

            var irrmmPak = paks.FirstOrDefault(p => Path.GetFileName(p).StartsWith("pakchunk999-IRR", StringComparison.OrdinalIgnoreCase)
                                                   || Path.GetFileName(p).Contains("IRRModMerger", StringComparison.OrdinalIgnoreCase)
                                                   || Path.GetFileName(p).Contains("irrmm", StringComparison.OrdinalIgnoreCase));
            if (irrmmPak != null)
            {
                try { File.Delete(irrmmPak); log($"Deleted old irrmm pak: {Path.GetFileName(irrmmPak)}"); } catch { }
            }

            // 2. Classify paks: + has irrmm data (contains Test_C/Content/.../Vendor jsons or metadata), - does not.
            // We treat any pak that repaks/unpacks successfully and contains a DT_Vendor-related asset as a "+" pak.
            var modPaks = paks.Where(p => !Path.GetFileName(p).Equals(Path.GetFileName(outputPakPath), StringComparison.OrdinalIgnoreCase)).ToList();

            // 3. Unpack each pak with repak
            if (string.IsNullOrWhiteSpace(repakPath) || !File.Exists(repakPath))
                throw new FileNotFoundException("repak.exe not found.", repakPath);

            foreach (var pak in modPaks)
            {
                var name = Path.GetFileNameWithoutExtension(pak);
                var dest = Path.Combine(unpackRoot, name);
                Directory.CreateDirectory(dest);
                RunProcess(repakPath, new[] { "unpack", pak, dest }, log);
            }
            log("Unpacked all mod paks.");

            // 4. Build UAsset dict: find all DT_Vendor.uasset among unpacked trees
            var vendorAssets = Directory.GetFiles(unpackRoot, "DT_Vendor.uasset", SearchOption.AllDirectories).ToList();
            log($"Found {vendorAssets.Count} DT_Vendor.uasset across mod paks.");

            // 5. Locate base game DT_Vendor.uasset (in game files / engine). Search the unpacked base game if present; otherwise skip merge.
            // We'll attempt to find a base DT_Vendor in the game folder's non-pak content (rare). If none, start from empty.
            // For IRR mods, the base game DT_Vendor is shipped inside a base pak; we look in unpacked trees of non-mod paks too (already unpacked above).

            // Collect all vendor struct rows from mod paks
            var mergedRows = new System.Collections.Generic.List<UAssetAPI.PropertyTypes.Structs.StructPropertyData>();

            // Track which paks had irrmm data (+) by presence of a "irrmm" marker file or vendor jsons
            foreach (var va in vendorAssets)
            {
                try
                {
                    // Determine engine version from project settings; default UE5_3
                    var version = EngineVersion.VER_UE5_6;
                    var asset = new UAsset(va, version, null);
                    // Find DataTable export
                    foreach (var export in asset.Exports)
                    {
                        if (export is DataTableExport dte && dte.Table != null)
                        {
                            foreach (var row in dte.Table.Data)
                            {
                                mergedRows.Add(row);
                            }
                            log($"  Loaded {dte.Table.Data.Count} rows from {Path.GetRelativePath(unpackRoot, va)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log($"  Failed to read {va}: {ex.Message}");
                }
            }

            // 6. Write merged DT_Vendor.uasset into temp dt_vendor tree mirroring game path
            // Find a template DT_Vendor to clone structure from (first vendor asset)
            string? templatePath = vendorAssets.FirstOrDefault();
            if (templatePath == null)
            {
                log("No DT_Vendor template found; skipping vendor merge.");
            }
            else
            {
                try
                {
                    var version = EngineVersion.VER_UE5_6;
                    var template = new UAsset(templatePath, version, null);
                    // Clear existing rows then add merged unique rows (by Name)
                    var seen = new System.Collections.Generic.HashSet<string>();
                    foreach (var export in template.Exports)
                    {
                        if (export is DataTableExport dte && dte.Table != null)
                        {
                            var unique = new System.Collections.Generic.List<UAssetAPI.PropertyTypes.Structs.StructPropertyData>();
                            foreach (var r in mergedRows)
                            {
                                var key = r.Name.ToString();
                                if (seen.Add(key)) unique.Add(r);
                            }
                            dte.Table.Data = unique;
                            log($"Merged DT_Vendor: {unique.Count} unique rows.");
                        }
                    }
                    var outDir = Path.Combine(dtVendorRoot, "Test_C", "Content", "Blueprints", "InventorySystem", "Vendor");
                    Directory.CreateDirectory(outDir);
                    var outPath = Path.Combine(outDir, "DT_Vendor.uasset");
                    template.Write(outPath);
                    log($"Wrote merged DT_Vendor to {outPath}");
                }
                catch (Exception ex)
                {
                    log($"Vendor merge failed: {ex.Message}");
                }
            }

            // 7. Copy all non-DT_Vendor unpacked assets into staging tree (passthrough) so repak repacks them
            var stagingRoot = Path.Combine(tempRoot, "staging");
            try { if (Directory.Exists(stagingRoot) && IsUnderDirectory(stagingRoot, tempRoot)) Directory.Delete(stagingRoot, true); } catch { }
            Directory.CreateDirectory(stagingRoot);
            var stagingContent = Path.Combine(stagingRoot, "Test_C", "Content");
            Directory.CreateDirectory(stagingContent);

            // Copy merged dt_vendor over
            CopyTree(Path.Combine(dtVendorRoot, "Test_C", "Content"), stagingContent);

            // Copy each unpacked mod's Test_C/Content into staging (skip DT_Vendor.uasset to avoid clobbering merged)
            foreach (var dir in Directory.GetDirectories(unpackRoot))
            {
                var contentDir = Path.Combine(dir, "Test_C", "Content");
                if (Directory.Exists(contentDir))
                {
                    foreach (var file in Directory.GetFiles(contentDir, "*.*", SearchOption.AllDirectories))
                    {
                        if (Path.GetFileName(file).Equals("DT_Vendor.uasset", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var rel = Path.GetRelativePath(contentDir, file);
                        var dest = Path.Combine(stagingContent, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(file, dest, true);
                    }
                }
            }
            log("Staged merged content.");

            // 8. Repak pack
            Directory.CreateDirectory(Path.GetDirectoryName(outputPakPath)!);
            if (File.Exists(outputPakPath)) File.Delete(outputPakPath);
            RunProcess(repakPath, new[] { "pack", stagingRoot, outputPakPath }, log);

            if (!File.Exists(outputPakPath))
                throw new Exception("repak did not produce output pak.");

            log($"Build complete: {Path.GetFileName(outputPakPath)}");

            // 9. Cleanup (keep temp for debugging per original keep_temp_folder=True)
            // leave tempRoot in place
        }

        private static void CopyTree(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            foreach (var file in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }

        private static bool IsUnderDirectory(string path, string root)
        {
            try
            {
                var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void RunProcess(string exe, IEnumerable<string> args, Action<string> log)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc == null) throw new Exception("Failed to start " + exe);
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (!string.IsNullOrWhiteSpace(stdout)) log(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) log("STDERR: " + stderr.Trim());
            if (proc.ExitCode != 0)
                throw new Exception($"{Path.GetFileName(exe)} failed (exit {proc.ExitCode}): {stderr}");
        }
    }
}