using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IrrModWorkbench.Services
{
    public class RecipeStep
    {
        public int Export { get; set; }
        public string ExportName { get; set; } = string.Empty;
        public string Property { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    public class Recipe
    {
        public string Title { get; set; } = string.Empty;
        public List<RecipeStep> Steps { get; set; } = new();
        public string Tip { get; set; } = string.Empty;
    }

    public static class RecipeService
    {
        private static Dictionary<string, List<Recipe>> _recipes = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Resources", "recipes.json");
                if (!File.Exists(path))
                {
                    // also try next to exe
                    path = Path.Combine(AppContext.BaseDirectory, "recipes.json");
                }
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<Recipe>>>(json);
                if (loaded != null)
                    _recipes = new Dictionary<string, List<Recipe>>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _recipes = new Dictionary<string, List<Recipe>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static List<Recipe> GetRecipes(string assetFileName)
        {
            Load();
            if (string.IsNullOrWhiteSpace(assetFileName)) return new List<Recipe>();
            if (_recipes.TryGetValue(assetFileName, out var list)) return new List<Recipe>(list);
            // fallback: match by filename substring
            foreach (var kv in _recipes)
            {
                if (assetFileName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return new List<Recipe>(kv.Value);
            }
            return new List<Recipe>();
        }

        public static string FormatSteps(Recipe recipe)
        {
            if (recipe == null) return string.Empty;
            var lines = new List<string>();
            for (int i = 0; i < recipe.Steps.Count; i++)
            {
                var s = recipe.Steps[i];
                lines.Add($"{i + 1}. Export [{s.Export}] {s.ExportName} -> {s.Property} ({s.Type}): {s.Value}  -- {s.Note}");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}