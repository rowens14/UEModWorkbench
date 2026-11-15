using System.IO;

namespace IrrModWorkbench.Services
{
    public static class ProjectService
    {
        public static void InitializeProject(string projectFolder)
        {
            Directory.CreateDirectory(projectFolder);
            Directory.CreateDirectory(Path.Combine(projectFolder, "SourceAssets"));
            Directory.CreateDirectory(Path.Combine(projectFolder, "ModifiedAssets"));
            Directory.CreateDirectory(Path.Combine(projectFolder, "Metadata"));
            Directory.CreateDirectory(Path.Combine(projectFolder, "Output"));
        }
    }
}
