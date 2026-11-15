using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using UAssetAPI;
using IrrModWorkbench.Services;

namespace IrrModWorkbench
{
    public partial class MainWindow
    {
        private void RefreshJsonView()
        {
            if (VM.CurrentAsset == null || JsonEditor == null) return;
            try
            {
                JsonEditor.Text = VM.CurrentAsset.SerializeJson(Newtonsoft.Json.Formatting.Indented);
                if (JsonEditor.SyntaxHighlighting == null)
                    JsonEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JSON");
            }
            catch (Exception ex)
            {
                VM.Log($"JSON refresh failed: {ex.Message}");
            }
        }

        private void Caret_PositionChanged(object? sender, EventArgs e)
        {
            if (JsonEditor == null) return;
            try
            {
                int caretOffset = JsonEditor.CaretOffset;
                int bracket = -1;
                if (caretOffset > 0 && IsBracketChar(JsonEditor.Document.GetCharAt(caretOffset - 1)))
                    bracket = caretOffset - 1;
                else if (caretOffset < JsonEditor.Document.TextLength && IsBracketChar(JsonEditor.Document.GetCharAt(caretOffset)))
                    bracket = caretOffset;

                int match = bracket >= 0 ? BracketMatcher.FindMatch(JsonEditor.Document, bracket) : -1;

                _bracketTransformer.CaretBracket = bracket;
                _bracketTransformer.MatchBracket = match;
                JsonEditor.TextArea.TextView.Redraw();
            }
            catch { }
        }

        private static bool IsBracketChar(char c) => c == '{' || c == '}' || c == '[' || c == ']' || c == '(' || c == ')';

        private void ScrollJsonToSelectedNode(TreeViewItem node)
        {
            if (JsonEditor == null || string.IsNullOrEmpty(JsonEditor.Text) || node == null) return;
            try
            {
                int startOffset = 0;
                string? token = null;

                if (node.Tag is UAssetAPI.ExportTypes.Export export)
                {
                    token = $"\"ObjectName\": \"{export.ObjectName}\"";
                }
                else if (node.Tag is UAssetAPI.PropertyTypes.Objects.PropertyData prop)
                {
                    var ancestorExport = FindAncestorExport(node);
                    if (ancestorExport != null)
                    {
                        var exportToken = $"\"ObjectName\": \"{ancestorExport.ObjectName}\"";
                        var ei = JsonEditor.Text.IndexOf(exportToken, StringComparison.Ordinal);
                        if (ei >= 0) startOffset = ei;
                    }
                    token = $"\"{prop.Name}\"";
                }

                if (string.IsNullOrEmpty(token)) return;

                var idx = JsonEditor.Text.IndexOf(token, startOffset, StringComparison.Ordinal);
                if (idx < 0 && startOffset > 0)
                    idx = JsonEditor.Text.IndexOf(token, StringComparison.Ordinal);
                if (idx < 0) return;

                JsonEditor.Select(idx, token.Length);
                var line = JsonEditor.Document.GetLineByOffset(idx);
                JsonEditor.ScrollToLine(line.LineNumber);
            }
            catch { }
        }

        private UAssetAPI.ExportTypes.Export? FindAncestorExport(TreeViewItem node)
        {
            var current = node.Parent as TreeViewItem;
            while (current != null)
            {
                if (current.Tag is UAssetAPI.ExportTypes.Export ex) return ex;
                current = current.Parent as TreeViewItem;
            }
            return null;
        }
    }
}
