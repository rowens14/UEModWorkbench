using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Unversioned;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace IrrModWorkbench.Services
{
    public static class AssetService
    {
        public static UAsset Load(string path, EngineVersion engineVersion, string? usmapPath)
        {
            UAssetAPI.Unversioned.Usmap? usmap = string.IsNullOrWhiteSpace(usmapPath) ? null : new UAssetAPI.Unversioned.Usmap(usmapPath);
            return new UAsset(path, engineVersion, usmap!, CustomSerializationFlags.None);
        }

        public static void PopulateTree(TreeView tree, UAsset asset, string filter)
        {
            tree.Items.Clear();
            filter = filter?.Trim() ?? string.Empty;

            var root = new TreeViewItem { Header = System.IO.Path.GetFileName(asset.FilePath), IsExpanded = true };
            AddSummary(root, asset);
            AddImports(root, asset, filter);
            AddExports(root, asset, filter);
            tree.Items.Add(root);
        }

        private static void AddSummary(TreeViewItem root, UAsset asset)
        {
            var summary = new TreeViewItem { Header = "Summary", IsExpanded = true };
            summary.Items.Add(new TreeViewItem { Header = $"Recorded Engine Version: {asset.RecordedEngineVersion}" });
            summary.Items.Add(new TreeViewItem { Header = $"Object Version: {asset.ObjectVersion}" });
            summary.Items.Add(new TreeViewItem { Header = $"Object Version UE5: {asset.ObjectVersionUE5}" });
            summary.Items.Add(new TreeViewItem { Header = $"Names: {asset.GetNameMapIndexList().Count}" });
            summary.Items.Add(new TreeViewItem { Header = $"Imports: {asset.Imports.Count}" });
            summary.Items.Add(new TreeViewItem { Header = $"Exports: {asset.Exports.Count}" });
            root.Items.Add(summary);
        }

        private static void AddImports(TreeViewItem root, UAsset asset, string filter)
        {
            var importsNode = new TreeViewItem { Header = "Imports", IsExpanded = false };
            for (int i = 0; i < asset.Imports.Count; i++)
            {
                var imp = asset.Imports[i];
                var header = $"[{i}] {imp.ObjectName} ({imp.ClassName})";
                if (!Matches(header, filter)) continue;
                importsNode.Items.Add(new TreeViewItem { Header = header });
            }
            if (importsNode.Items.Count > 0 || string.IsNullOrEmpty(filter))
                root.Items.Add(importsNode);
        }

        private static void AddExports(TreeViewItem root, UAsset asset, string filter)
        {
            var exportsNode = new TreeViewItem { Header = "Exports", IsExpanded = true };
            for (int i = 0; i < asset.Exports.Count; i++)
            {
                var export = asset.Exports[i];
                var exportDisplay = new IrrModWorkbench.Models.TreeNodeDisplay
                {
                    Name = $"[{i}] {export.ObjectName}",
                    Type = export.GetType().Name,
                    Tag = export
                };
                var exportNode = new TreeViewItem
                {
                    Header = exportDisplay,
                    IsExpanded = true,
                    Tag = export
                };

                if (export is NormalExport normal)
                {
                    foreach (var prop in normal.Data)
                        AddProperty(exportNode, prop, filter, $"Export[{i}]");
                }

                if (exportNode.Items.Count > 0 || string.IsNullOrEmpty(filter))
                {
                    if (string.IsNullOrEmpty(filter) || exportNode.Items.Count > 0 || Matches(exportDisplay.DisplayText, filter))
                        exportsNode.Items.Add(exportNode);
                }
            }
            root.Items.Add(exportsNode);
        }

        private static void AddProperty(TreeViewItem parent, PropertyData prop, string filter, string path)
        {
            var currentPath = $"{path}/{prop.Name}";
            var valuePreview = GetPreview(prop);
            var display = new IrrModWorkbench.Models.TreeNodeDisplay
            {
                Name = prop.Name?.ToString() ?? string.Empty,
                Type = prop.PropertyType?.ToString() ?? string.Empty,
                Preview = valuePreview,
                Tag = prop
            };
            var header = display.DisplayText;

            if (!string.IsNullOrEmpty(filter) && !Matches(header, filter) && !HasMatchingChild(prop, filter))
                return;

            var node = new TreeViewItem { Header = display, IsExpanded = false, Tag = prop };
            parent.Items.Add(node);

            if (prop is ArrayPropertyData arr && arr.Value != null)
            {
                for (int i = 0; i < arr.Value.Length; i++)
                {
                    var child = arr.Value[i];
                    AddProperty(node, child, filter, $"{currentPath}[{i}]");
                }
            }
            else if (prop is StructPropertyData st && st.Value != null)
            {
                foreach (var child in st.Value)
                    AddProperty(node, child, filter, $"{currentPath}/{child.Name}");
            }
            else if (prop is MapPropertyData map && map.Value != null)
            {
                foreach (var kv in map.Value)
                {
                    var keyPreview = GetPreview(kv.Key);
                    var valuePreview2 = GetPreview(kv.Value);
                    var childHeader = $"{kv.Key.Name} => {kv.Value.Name}: {valuePreview2}";
                    var childNode = new TreeViewItem { Header = childHeader };
                    node.Items.Add(childNode);
                    AddProperty(childNode, kv.Value, filter, $"{currentPath}/{kv.Key.Name}");
                }
            }
            else if (prop is SetPropertyData set && set.Value != null)
            {
                for (int i = 0; i < set.Value.Length; i++)
                    AddProperty(node, set.Value[i], filter, $"{currentPath}[{i}]");
            }
        }

        private static bool HasMatchingChild(PropertyData prop, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (prop is ArrayPropertyData arr && arr.Value != null)
                return arr.Value.Any(p => Matches($"{p.Name} : {p.PropertyType}", filter) || HasMatchingChild(p, filter));
            if (prop is StructPropertyData st && st.Value != null)
                return st.Value.Any(p => Matches($"{p.Name} : {p.PropertyType}", filter) || HasMatchingChild(p, filter));
            if (prop is MapPropertyData map && map.Value != null)
                return map.Value.Keys.Any(k => Matches(k.Name.ToString(), filter));
            if (prop is SetPropertyData set && set.Value != null)
                return set.Value.Any(p => Matches($"{p.Name} : {p.PropertyType}", filter) || HasMatchingChild(p, filter));
            return false;
        }

        private static bool Matches(string text, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return text.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPreview(PropertyData prop)
        {
            try
            {
                var raw = prop.RawValue;
                if (raw == null) return string.Empty;
                var s = raw.ToString();
                if (string.IsNullOrEmpty(s)) return string.Empty;
                return s.Length > 80 ? s.Substring(0, 77) + "..." : s;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}