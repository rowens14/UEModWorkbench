using System;
using System.IO;
using UAssetAPI;

namespace IrrModWorkbench.Services
{
    public static class AssetPersistenceService
    {
        public static string Save(UAsset asset, string path, string? projectFolder)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));

            // Repair enums before writing.
            RepairEnums(asset);

            // Back up the original once (never overwrite an existing backup).
            if (File.Exists(path) && !File.Exists(path + ".bak"))
            {
                File.Copy(path, path + ".bak", true);
            }

            // Write to ModifiedAssets only — never overwrite the source file.
            string dest;
            if (!string.IsNullOrWhiteSpace(projectFolder))
            {
                dest = ResolveSaveDestination(projectFolder, asset, path);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            }
            else
            {
                dest = path;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            }

            try
            {
                var tempDest = Path.Combine(Path.GetDirectoryName(dest)!, Path.GetFileNameWithoutExtension(dest) + ".tmp.uasset");

                try { File.Delete(tempDest); } catch { }
                try { File.Delete(Path.ChangeExtension(tempDest, ".uexp")); } catch { }

                asset.Write(tempDest);

                var tempUexp = Path.ChangeExtension(tempDest, ".uexp");
                var finalUexp = Path.ChangeExtension(dest, ".uexp");

                File.Move(tempDest, dest, overwrite: true);
                if (File.Exists(tempUexp))
                    File.Move(tempUexp, finalUexp, overwrite: true);
            }
            catch (Exception writeEx)
            {
                try { File.Delete(dest + ".tmp"); } catch { }
                throw new IOException($"Save failed: {writeEx.Message}", writeEx);
            }

            return dest;
        }

        public static string ResolveSaveDestination(string projectFolder, UAsset asset, string originalPath)
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

        private static void RepairEnums(UAsset asset)
        {
            if (asset == null) return;
            foreach (var export in asset.Exports)
            {
                if (export is UAssetAPI.ExportTypes.NormalExport ne && ne.Data != null)
                    foreach (var p in ne.Data)
                        RepairProperty(p, asset);
            }
        }

        private static void RepairProperty(UAssetAPI.PropertyTypes.Objects.PropertyData prop, UAsset asset)
        {
            // Minimal: recurse containers (same as original RepairProperty).
            if (prop is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arr && arr.Value != null)
                foreach (var c in arr.Value) RepairProperty(c, asset);
            if (prop is UAssetAPI.PropertyTypes.Structs.StructPropertyData st && st.Value != null)
                foreach (var c in st.Value) RepairProperty(c, asset);
            if (prop is UAssetAPI.PropertyTypes.Objects.MapPropertyData mp && mp.Value != null)
                foreach (var kv in mp.Value) RepairProperty(kv.Value, asset);
            if (prop is UAssetAPI.PropertyTypes.Objects.SetPropertyData sp && sp.Value != null)
                foreach (var c in sp.Value) RepairProperty(c, asset);
        }

        private static string? GetAssetRelativeGamePath(UAsset? asset, string? extension)
        {
            if (asset == null) return null;
            try
            {
                var ext = string.IsNullOrWhiteSpace(extension) ? ".uasset" : extension;
                if (!ext.StartsWith(".")) ext = "." + ext;
                var fileName = string.Empty;
                try { if (!string.IsNullOrWhiteSpace(asset.FilePath)) fileName = Path.GetFileNameWithoutExtension(asset.FilePath); } catch { }
                var names = asset.GetNameMapIndexList();
                foreach (var n in names)
                {
                    var s = n?.ToString() ?? string.Empty;
                    if (!s.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(fileName) && s.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                        return s.Substring("/Game/".Length).Replace('/', Path.DirectorySeparatorChar) + ext;
                }
                foreach (var n in names)
                {
                    var s = n?.ToString() ?? string.Empty;
                    if (s.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
                        return s.Substring("/Game/".Length).Replace('/', Path.DirectorySeparatorChar) + ext;
                }
            }
            catch { }
            return null;
        }
    }
}