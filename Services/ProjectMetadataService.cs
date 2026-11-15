using System;
using System.IO;
using System.Text.Json;

namespace IrrModWorkbench.Services
{
    public class ProjectMetadata
    {
        public string? UsmapPath { get; set; }
        public DateTime? UsmapLastModified { get; set; }
        public string? GameVersion { get; set; }
    }

    public static class ProjectMetadataService
    {
        private static string GetPath(string projectFolder) =>
            Path.Combine(projectFolder, "Metadata", "project.json");

        public static ProjectMetadata Load(string projectFolder)
        {
            var path = GetPath(projectFolder);
            if (!File.Exists(path)) return new ProjectMetadata();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ProjectMetadata>(json) ?? new ProjectMetadata();
            }
            catch { return new ProjectMetadata(); }
        }

        public static void Save(string projectFolder, ProjectMetadata metadata)
        {
            var path = GetPath(projectFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}