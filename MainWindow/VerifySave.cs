using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using IrrModWorkbench.Services;
using IrrModWorkbench.Models;

namespace IrrModWorkbench
{
    public partial class MainWindow
    {


        private int ApplyJsonToAsset(UAssetAPI.UAsset asset, Newtonsoft.Json.Linq.JObject json, bool dryRun, List<string>? diffs, List<PropertyChange>? changes)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (json == null) throw new ArgumentNullException(nameof(json));

            var exportsJson = json["Exports"] as Newtonsoft.Json.Linq.JArray;
            if (exportsJson == null) throw new Exception("JSON has no 'Exports' array.");

            int changed = 0;
            foreach (var export in asset.Exports)
            {
                if (export is not UAssetAPI.ExportTypes.NormalExport ne || ne.Data == null) continue;

                Newtonsoft.Json.Linq.JObject? match = null;
                foreach (var je in exportsJson)
                {
                    if (je is Newtonsoft.Json.Linq.JObject jo &&
                        string.Equals(jo["ObjectName"]?.ToString(), export.ObjectName?.ToString(), StringComparison.Ordinal))
                    {
                        match = (Newtonsoft.Json.Linq.JObject)jo;
                        break;
                    }
                }
                if (match == null) continue;

                var dataJson = match["Data"] as Newtonsoft.Json.Linq.JArray;
                if (dataJson == null) continue;

                foreach (var prop in ne.Data)
                {
                    Newtonsoft.Json.Linq.JObject? pj = null;
                    foreach (var jp in dataJson)
                    {
                        if (jp is Newtonsoft.Json.Linq.JObject jpo &&
                            string.Equals(jpo["Name"]?.ToString(), prop.Name?.ToString(), StringComparison.Ordinal))
                        {
                            pj = (Newtonsoft.Json.Linq.JObject)jp;
                            break;
                        }
                    }
                    if (pj == null) continue;

                    var propertySelector = $"$.Exports[*].Data[?(@.Name == '{prop.Name}')]";
                    var localDiffs = dryRun ? new List<string>() : null;
                    int d = PatchProperty(prop, pj, asset, dryRun, localDiffs, dryRun ? null : changes, propertySelector);
                    changed += d;
                    if (dryRun && localDiffs != null && localDiffs.Count > 0 && diffs != null)
                    {
                        foreach (var ld in localDiffs)
                            diffs.Add($"  {export.ObjectName}.{ld.TrimStart()}");
                    }
                }

                if (dryRun)
                {
                    foreach (var jp in dataJson)
                    {
                        if (jp is not Newtonsoft.Json.Linq.JObject jpo) continue;
                        var jpName = jpo["Name"]?.ToString();
                        if (string.IsNullOrWhiteSpace(jpName)) continue;
                        if (ne.Data.Any(p => string.Equals(p.Name?.ToString(), jpName, StringComparison.Ordinal))) continue;
                        if (IsSupportedPropertyType(jpo) && diffs != null)
                            diffs.Add($"  {export.ObjectName}.(add) {jpName}");
                    }
                }
                else
                {
                    foreach (var jp in dataJson)
                    {
                        if (jp is not Newtonsoft.Json.Linq.JObject jpo) continue;
                        var jpName = jpo["Name"]?.ToString();
                        if (string.IsNullOrWhiteSpace(jpName)) continue;
                        if (ne.Data.Any(p => string.Equals(p.Name?.ToString(), jpName, StringComparison.Ordinal))) continue;
                        var created = CreatePropertyFromJson(asset, jpo);
                        if (created != null)
                        {
                            ne.Data.Add(created);
                            changed++;
                            VM.Log($"  {export.ObjectName}.{jpName}: added");
                        }
                    }
                }
            }

            return changed;
        }

        private bool ApplyJsonChangesToAsset(bool promptConfirmation)
        {
            if (VM.CurrentAsset == null) { VM.Log("No asset loaded."); return false; }
            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(JsonEditor.Text);

                var diffs = new List<string>();
                int dryChanged = ApplyJsonToAsset(VM.CurrentAsset, json, true, diffs, null);

                if (diffs.Count == 0)
                {
                    VM.Log("Save Asset: no JSON changes detected.");
                    return true;
                }

                VM.Log($"Save Asset: {diffs.Count} change(s) detected:");
                foreach (var d in diffs) VM.Log(d);

                if (promptConfirmation)
                {
                    var result = MessageBox.Show(
                        "Changes that will be applied:\n" + string.Join("\n", diffs) + "\nReady to save?",
                        "Save Asset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes)
                    {
                        VM.Log("Changes discarded.");
                        return false;
                    }
                }

                int changed = ApplyJsonToAsset(VM.CurrentAsset, json, false, null, null);

                AssetService.PopulateTree(AssetTree, VM.CurrentAsset, string.Empty);
                RefreshJsonView();

                if (changed > 0)
                {
                    VM.IsDirty = true;
                    if (VM.ActiveTab != null) VM.ActiveTab.IsDirty = true;
                    UpdateTitle();
                }
                VM.Log($"Applied: {changed} value(s).");
                return true;
            }
            catch (Exception ex)
            {
                VM.Log($"Save Asset (apply) failed: {ex.Message}");
                MessageBox.Show(ex.Message, "Save Asset", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool IsSupportedPropertyType(Newtonsoft.Json.Linq.JObject jp)
        {
            var typeHint = (jp["$type"]?.ToString() ?? jp["PropertyType"]?.ToString() ?? string.Empty).ToLowerInvariant();
                var simpleType = typeHint.Split(',')[0].Split('.').Last();
            var valToken = jp["Value"] ?? jp["RawValue"];
            if (typeHint.Contains("bool") || typeHint.Contains("int64") || typeHint.Contains("int") || typeHint.Contains("float")
                || typeHint.Contains("double") || typeHint.Contains("byte") || typeHint.Contains("str") || typeHint.Contains("string")
                || typeHint.Contains("name") || typeHint.Contains("text") || typeHint.Contains("struct")
                || typeHint.Contains("array") || typeHint.Contains("map") || typeHint.Contains("set"))
                return true;
            if (valToken != null && (valToken.Type == Newtonsoft.Json.Linq.JTokenType.Boolean
                || valToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer
                || valToken.Type == Newtonsoft.Json.Linq.JTokenType.Float
                || valToken.Type == Newtonsoft.Json.Linq.JTokenType.String))
                return true;
            return false;
        }

        private UAssetAPI.PropertyTypes.Objects.PropertyData? CreatePropertyFromJson(UAssetAPI.UAsset asset, Newtonsoft.Json.Linq.JObject jp)
        {
            var name = jp["Name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                var fname = new UAssetAPI.UnrealTypes.FName(asset, name);
                var typeHint = (jp["$type"]?.ToString() ?? jp["PropertyType"]?.ToString() ?? string.Empty).ToLowerInvariant();
                var valToken = jp["Value"] ?? jp["RawValue"];
                var simpleType = typeHint.Split(',')[0].Split('.').Last();

                UAssetAPI.PropertyTypes.Objects.PropertyData? prop = null;
                // Complex types.
                if (simpleType.Contains("struct"))
                {
                    var structProp = new UAssetAPI.PropertyTypes.Structs.StructPropertyData(fname);
                    var structTypeName = jp["StructType"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(structTypeName))
                        structProp.StructType = new UAssetAPI.UnrealTypes.FName(asset, structTypeName, 0);
                    var children = jp["Value"] as Newtonsoft.Json.Linq.JArray;
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            if (child is Newtonsoft.Json.Linq.JObject childObj)
                            {
                                var created = CreatePropertyFromJson(asset, childObj);
                                if (created != null) structProp.Value.Add(created);
                            }
                        }
                    }
                    prop = structProp;
                }
                else if (simpleType.Contains("array"))
                {
                    var arrProp = new UAssetAPI.PropertyTypes.Objects.ArrayPropertyData(fname);
                    var arrType = jp["ArrayType"]?.ToString();
                    if (string.IsNullOrWhiteSpace(arrType))
                    {
                        var firstChild = jp["Value"] is Newtonsoft.Json.Linq.JArray ja && ja.Count > 0 ? ja[0] as Newtonsoft.Json.Linq.JObject : null;
                        var ft = (firstChild?["$type"]?.ToString() ?? firstChild?["PropertyType"]?.ToString() ?? string.Empty).ToLowerInvariant();
                        if (ft.Contains("struct")) arrType = "StructProperty";
                        else if (ft.Contains("bool")) arrType = "BoolProperty";
                        else if (ft.Contains("int64")) arrType = "Int64Property";
                        else if (ft.Contains("int")) arrType = "IntProperty";
                        else if (ft.Contains("float")) arrType = "FloatProperty";
                        else if (ft.Contains("double")) arrType = "DoubleProperty";
                        else if (ft.Contains("byte")) arrType = "ByteProperty";
                        else if (ft.Contains("str") || ft.Contains("string")) arrType = "StrProperty";
                        else if (ft.Contains("name")) arrType = "NameProperty";
                        else if (ft.Contains("text")) arrType = "TextProperty";
                        else if (ft.Contains("object")) arrType = "ObjectProperty";
                        else arrType = "StructProperty";
                    }
                    if (!string.IsNullOrWhiteSpace(arrType))
                        arrProp.ArrayType = new UAssetAPI.UnrealTypes.FName(asset, arrType, 0);
                    var arrChildren = jp["Value"] as Newtonsoft.Json.Linq.JArray;
                    if (arrChildren != null && arrChildren.Count > 0 && arrChildren[0] is Newtonsoft.Json.Linq.JObject firstObj && arrType == "StructProperty")
                    {
                        var stName = firstObj["StructType"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(stName))
                        {
                            var dummy = new UAssetAPI.PropertyTypes.Structs.StructPropertyData(new UAssetAPI.UnrealTypes.FName(asset, "Dummy", 0));
                            dummy.StructType = new UAssetAPI.UnrealTypes.FName(asset, stName, 0);
                            dummy.Value = new System.Collections.Generic.List<UAssetAPI.PropertyTypes.Objects.PropertyData>();
                            arrProp.DummyStruct = dummy;
                        }
                    }
                    var children = jp["Value"] as Newtonsoft.Json.Linq.JArray;
                    if (children != null)
                    {
                        var list = new System.Collections.Generic.List<UAssetAPI.PropertyTypes.Objects.PropertyData>();
                        foreach (var child in children)
                        {
                            if (child is Newtonsoft.Json.Linq.JObject childObj)
                            {
                                var created = CreatePropertyFromJson(asset, childObj);
                                if (created != null) list.Add(created);
                            }
                        }
                        arrProp.Value = list.ToArray();
                    }
                    prop = arrProp;
                }
                else if (simpleType.Contains("map"))
                {
                    var mapProp = new UAssetAPI.PropertyTypes.Objects.MapPropertyData(fname);
                    var keyType = jp["KeyType"]?.ToString();
                    var valType = jp["ValueType"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(keyType)) mapProp.KeyType = new UAssetAPI.UnrealTypes.FName(asset, keyType, 0);
                    if (!string.IsNullOrWhiteSpace(valType)) mapProp.ValueType = new UAssetAPI.UnrealTypes.FName(asset, valType, 0);
                    // Map entries are not fully supported via this simple path; leave empty.
                    prop = mapProp;
                }
                else if (simpleType.Contains("object"))
                {
                    var objProp = new UAssetAPI.PropertyTypes.Objects.ObjectPropertyData(fname);
                    if (valToken != null && valToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                    {
                        try { objProp.Value = new UAssetAPI.UnrealTypes.FPackageIndex(valToken.ToObject<int>()); } catch { }
                        try { objProp.RawValue = objProp.Value; } catch { }
                    }
                    prop = objProp;
                }
                else if (simpleType.Contains("enum"))
                {
                    var enumProp = new UAssetAPI.PropertyTypes.Objects.EnumPropertyData(fname);
                    var enumType = jp["EnumType"]?.ToString();
                    var innerType = jp["InnerType"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(enumType)) enumProp.EnumType = new UAssetAPI.UnrealTypes.FName(asset, enumType, 0);
                    if (!string.IsNullOrWhiteSpace(innerType)) enumProp.InnerType = new UAssetAPI.UnrealTypes.FName(asset, innerType, 0);
                    if (valToken != null && valToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        var fn = new UAssetAPI.UnrealTypes.FName(asset, valToken.ToString(), 0);
                        enumProp.Value = fn;
                        enumProp.RawValue = fn;
                    }
                    prop = enumProp;
                }
                else if (simpleType.Contains("softobject") || simpleType.Contains("softasset"))
                {
                    var softProp = new UAssetAPI.PropertyTypes.Objects.SoftObjectPropertyData(fname);
                    prop = softProp;
                }
                else if (simpleType.Contains("text"))
                {
                    var textProp = new UAssetAPI.PropertyTypes.Objects.TextPropertyData(fname);
                    var cultureInvariant = jp["CultureInvariantString"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(cultureInvariant)) textProp.CultureInvariantString = new UAssetAPI.UnrealTypes.FString(cultureInvariant, System.Text.Encoding.UTF8);
                    prop = textProp;
                }
                else if (simpleType.Contains("set"))
                {
                    var setProp = new UAssetAPI.PropertyTypes.Objects.SetPropertyData(fname);
                    var setType = jp["ArrayType"]?.ToString();
                    if (string.IsNullOrWhiteSpace(setType))
                    {
                        var firstChild = jp["Value"] is Newtonsoft.Json.Linq.JArray ja && ja.Count > 0 ? ja[0] as Newtonsoft.Json.Linq.JObject : null;
                        var ft = (firstChild?["$type"]?.ToString() ?? firstChild?["PropertyType"]?.ToString() ?? string.Empty).ToLowerInvariant();
                        if (ft.Contains("struct")) setType = "StructProperty";
                        else if (ft.Contains("bool")) setType = "BoolProperty";
                        else if (ft.Contains("int")) setType = "IntProperty";
                        else if (ft.Contains("float")) setType = "FloatProperty";
                        else if (ft.Contains("object")) setType = "ObjectProperty";
                        else setType = "StructProperty";
                    }
                    if (!string.IsNullOrWhiteSpace(setType))
                        setProp.ArrayType = new UAssetAPI.UnrealTypes.FName(asset, setType, 0);
                    var children = jp["Value"] as Newtonsoft.Json.Linq.JArray;
                    if (children != null)
                    {
                        var list = new System.Collections.Generic.List<UAssetAPI.PropertyTypes.Objects.PropertyData>();
                        foreach (var child in children)
                        {
                            if (child is Newtonsoft.Json.Linq.JObject childObj)
                            {
                                var created = CreatePropertyFromJson(asset, childObj);
                                if (created != null) list.Add(created);
                            }
                        }
                        setProp.Value = list.ToArray();
                    }
                    prop = setProp;
                }
                if (prop == null)
                {
                if (typeHint.Contains("bool")) prop = new UAssetAPI.PropertyTypes.Objects.BoolPropertyData(fname);
                else if (typeHint.Contains("int64")) prop = new UAssetAPI.PropertyTypes.Objects.Int64PropertyData(fname);
                else if (typeHint.Contains("int")) prop = new UAssetAPI.PropertyTypes.Objects.IntPropertyData(fname);
                else if (typeHint.Contains("float")) prop = new UAssetAPI.PropertyTypes.Objects.FloatPropertyData(fname);
                else if (typeHint.Contains("double")) prop = new UAssetAPI.PropertyTypes.Objects.DoublePropertyData(fname);
                else if (typeHint.Contains("byte")) prop = new UAssetAPI.PropertyTypes.Objects.BytePropertyData(fname);
                else if (typeHint.Contains("str") || typeHint.Contains("string")) prop = new UAssetAPI.PropertyTypes.Objects.StrPropertyData(fname);
                else if (typeHint.Contains("name")) prop = new UAssetAPI.PropertyTypes.Objects.NamePropertyData(fname);
                else if (typeHint.Contains("text")) prop = new UAssetAPI.PropertyTypes.Objects.TextPropertyData(fname);

                // Fallback by value token type.
                if (prop == null && valToken != null)
                {
                    if (valToken.Type == Newtonsoft.Json.Linq.JTokenType.Boolean) prop = new UAssetAPI.PropertyTypes.Objects.BoolPropertyData(fname);
                    else if (valToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer) prop = new UAssetAPI.PropertyTypes.Objects.IntPropertyData(fname);
                    else if (valToken.Type == Newtonsoft.Json.Linq.JTokenType.Float) prop = new UAssetAPI.PropertyTypes.Objects.FloatPropertyData(fname);
                    else if (valToken.Type == Newtonsoft.Json.Linq.JTokenType.String) prop = new UAssetAPI.PropertyTypes.Objects.StrPropertyData(fname);
                }

                }
                if (prop == null)
                {
                    VM.Log($"  CreateProperty: unsupported type for '{name}'.");
                    return null;
                }

                try { prop.ArrayIndex = jp["ArrayIndex"]?.ToObject<int>() ?? 0; } catch { }

                if (valToken != null && valToken.Type != Newtonsoft.Json.Linq.JTokenType.Array && valToken.Type != Newtonsoft.Json.Linq.JTokenType.Object && !typeHint.Contains("object"))
                {
                    try
                    {
                        var parsed = ParsePropertyValue(prop, valToken.ToString());
                        prop.RawValue = parsed;
                        TrySetPropertyValue(prop, parsed, asset);
                        // Value set via TrySetPropertyValue above.




                    }
                    catch (Exception ex) { VM.Log($"  CreateProperty parse failed for '{name}': {ex.Message}"); }
                }

                VM.Log($"  CreateProperty: '{name}' -> {prop.GetType().Name} (hint: {typeHint})");
                return prop;
            }
            catch (Exception ex)
            {
                VM.Log($"  CreateProperty failed for '{name}': {ex.Message}");
                return null;
            }
        }

        private int PatchProperty(UAssetAPI.PropertyTypes.Objects.PropertyData assetProp, Newtonsoft.Json.Linq.JObject jsonProp, UAssetAPI.UAsset asset, bool dryRun, List<string>? diffs, List<PropertyChange>? changes, string selector)
        {
            int changed = 0;
            if (assetProp == null || jsonProp == null) return 0;

            // Primitive value
            bool isPrimitive = assetProp.RawValue is bool || assetProp.RawValue is int || assetProp.RawValue is float
                || assetProp.RawValue is double || assetProp.RawValue is byte || assetProp.RawValue is long
                || assetProp.RawValue is string || assetProp is UAssetAPI.PropertyTypes.Objects.EnumPropertyData;

            var newValToken = jsonProp["Value"] ?? jsonProp["RawValue"];

            if (isPrimitive && newValToken != null && newValToken.Type != Newtonsoft.Json.Linq.JTokenType.Array && newValToken.Type != Newtonsoft.Json.Linq.JTokenType.Object)
            {
                string newValStr = newValToken.ToString();
                string curValStr = assetProp.RawValue?.ToString() ?? string.Empty;
                if (newValStr != curValStr)
                {
                    if (dryRun)
                    {
                        diffs?.Add($"  {assetProp.Name}: {curValStr} -> {newValStr}");
                        changed++;
                    }
                    else
                    {
                        try
                        {
                            var parsed = ParsePropertyValue(assetProp, newValStr);
                             assetProp.RawValue = parsed;
                             TrySetPropertyValue(assetProp, parsed, asset);
                             changed++;
                            VM.Log($"  {assetProp.Name}: {curValStr} -> {newValStr}");
                            if (changes != null)
                            {
                                var vt = assetProp.RawValue is bool ? "bool"
                                    : assetProp.RawValue is int ? "int"
                                    : assetProp.RawValue is float ? "float"
                                    : assetProp.RawValue is double ? "double"
                                    : assetProp is UAssetAPI.PropertyTypes.Objects.EnumPropertyData ? "enum"
                                    : "string";
                                changes.Add(new PropertyChange(selector, assetProp.Name?.ToString() ?? string.Empty, curValStr, newValStr, vt));
                            }
                        }
                        catch (Exception ex) { VM.Log($"  {assetProp.Name}: parse failed ({ex.Message})"); }
                    }
                }
                return changed;
            }

            // Array
            if (assetProp is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arr && arr.Value != null && newValToken is Newtonsoft.Json.Linq.JArray jArr)
            {
                for (int i = 0; i < arr.Value.Length && i < jArr.Count; i++)
                {
                    if (jArr[i] is Newtonsoft.Json.Linq.JObject childJson)
                    {
                        var childSelector = $"{selector}.Value[{i}]";
                        if (arr.Value[i] is UAssetAPI.PropertyTypes.Structs.StructPropertyData stElem && stElem.StructType != null)
                            childSelector = $"{selector}.Value[?(@.StructType == '{stElem.StructType}')]";
                        changed += PatchProperty(arr.Value[i], childJson, asset, dryRun, diffs, changes, childSelector);
                    }
                }
                return changed;
            }

            // Struct
            if (assetProp is UAssetAPI.PropertyTypes.Structs.StructPropertyData st && st.Value != null)
            {
                var childrenJson = newValToken as Newtonsoft.Json.Linq.JArray;
                if (childrenJson == null) childrenJson = jsonProp["Value"] as Newtonsoft.Json.Linq.JArray;
                if (childrenJson != null)
                {
                    foreach (var child in st.Value)
                    {
                        Newtonsoft.Json.Linq.JObject? cj = null;
                        foreach (var jp in childrenJson)
                        {
                            if (jp is Newtonsoft.Json.Linq.JObject jpo &&
                                string.Equals(jpo["Name"]?.ToString(), child.Name?.ToString(), StringComparison.Ordinal))
                            {
                                cj = jpo;
                                break;
                            }
                        }
                        if (cj != null)
                        {
                            var childSelector = $"{selector}.Value[?(@.Name == '{child.Name}')]";
                            changed += PatchProperty(child, cj, asset, dryRun, diffs, changes, childSelector);
                        }
                    }

                    // Support adding new properties inside the struct.
                    foreach (var jp in childrenJson)
                    {
                        if (jp is not Newtonsoft.Json.Linq.JObject jpo) continue;
                        var jpName = jpo["Name"]?.ToString();
                        if (string.IsNullOrWhiteSpace(jpName)) continue;
                        if (st.Value.Any(c => string.Equals(c.Name?.ToString(), jpName, StringComparison.Ordinal))) continue;
                        var created = CreatePropertyFromJson(asset, jpo);
                        if (created == null) continue;
                        if (dryRun)
                        {
                            diffs?.Add($"  {assetProp.Name}.(add) {jpName}");
                            changed++;
                        }
                        else
                        {
                            try { st.Value.Add(created); } catch { /* fall through */ }
                            // If Value is not a List, rebuild as List via reflection.
                            if (!st.Value.Contains(created))
                            {
                                try
                                {
                                    var list = new List<UAssetAPI.PropertyTypes.Objects.PropertyData>(st.Value) { created };
                                    st.Value = list;
                                }
                                catch { }
                            }
                            changed++;
                            VM.Log($"  {assetProp.Name}.{jpName}: added");
                        }
                    }
                }
                return changed;
            }

            // Map
            if (assetProp is UAssetAPI.PropertyTypes.Objects.MapPropertyData mp && mp.Value != null)
            {
                var childrenJson = newValToken as Newtonsoft.Json.Linq.JArray;
                if (childrenJson == null) childrenJson = jsonProp["Value"] as Newtonsoft.Json.Linq.JArray;
                if (childrenJson != null)
                {
                    int idx = 0;
                    foreach (var kv in mp.Value)
                    {
                        if (idx < childrenJson.Count && childrenJson[idx] is Newtonsoft.Json.Linq.JObject cj)
                        {
                            var mapSelector = $"{selector}.Value[{idx}]";
                            changed += PatchProperty(kv.Value, cj, asset, dryRun, diffs, changes, mapSelector);
                        }
                        idx++;
                    }
                }
                return changed;
            }

            // Set
            if (assetProp is UAssetAPI.PropertyTypes.Objects.SetPropertyData sp && sp.Value != null)
            {
                var childrenJson = newValToken as Newtonsoft.Json.Linq.JArray;
                if (childrenJson == null) childrenJson = jsonProp["Value"] as Newtonsoft.Json.Linq.JArray;
                if (childrenJson != null)
                {
                    for (int i = 0; i < sp.Value.Length && i < childrenJson.Count; i++)
                    {
                        if (childrenJson[i] is Newtonsoft.Json.Linq.JObject cj)
                        {
                            var setSelector = $"{selector}.Value[{i}]";
                            changed += PatchProperty(sp.Value[i], cj, asset, dryRun, diffs, changes, setSelector);
                        }
                    }
                }
                return changed;
            }

            return changed;
        }

        private static void TrySetPropertyValue(
            UAssetAPI.PropertyTypes.Objects.PropertyData prop,
            object? parsed,
            UAssetAPI.UAsset asset)
        {
            try
            {
            if (parsed == null) return;
                var pi = prop.GetType().GetProperty("Value");
                if (pi == null || !pi.CanWrite) return;

                var targetType = pi.PropertyType;
                object? toSet = parsed;

                if (!targetType.IsAssignableFrom(parsed.GetType()))
                {
                    if (targetType == typeof(string))
                        toSet = parsed.ToString();
                    else if (targetType == typeof(UAssetAPI.UnrealTypes.FString) && parsed is string s)
                        toSet = new UAssetAPI.UnrealTypes.FString(s, System.Text.Encoding.UTF8);
                    else if (targetType == typeof(UAssetAPI.UnrealTypes.FName) && parsed is string s2)
                        toSet = new UAssetAPI.UnrealTypes.FName(asset, s2, 0);
                    else if (targetType.IsEnum)
                        toSet = Enum.Parse(targetType, parsed.ToString()!);
                    else
                        toSet = Convert.ChangeType(parsed, targetType);
                }

                pi.SetValue(prop, toSet);
            }
            catch
            {
                // Best effort; RawValue was already set.
            }
        }
        private static string? GetAssetRelativeGamePath(UAssetAPI.UAsset? asset, string? extension)
        {
            if (asset == null) return null;
            try
            {
                var ext = string.IsNullOrWhiteSpace(extension) ? ".uasset" : extension;
                if (!ext.StartsWith(".")) ext = "." + ext;
                var fileName = string.Empty;
                try { if (!string.IsNullOrWhiteSpace(asset.FilePath)) fileName = Path.GetFileNameWithoutExtension(asset.FilePath); } catch { }
                var names = asset.GetNameMapIndexList();
                // Prefer an entry that ends with the asset's own name.
                foreach (var n in names)
                {
                    var s = n?.ToString() ?? string.Empty;
                    if (!s.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(fileName) && s.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                        return s.Substring("/Game/".Length).Replace('/', Path.DirectorySeparatorChar) + ext;
                }
                // Fallback: first /Game/ entry.
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
