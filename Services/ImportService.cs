using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IrrModWorkbench.Services
{
    public class ImportResult
    {
        public int ImportedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> SkippedFiles { get; set; } = new();
    }

    public static class ImportService
    {
        public static int ImportAssetFolder(string sourceFolder, string destinationFolder)
        {
            var result = ImportAssetFolderWithResult(sourceFolder, destinationFolder);
            return result.ImportedCount;
        }

        public static ImportResult ImportAssetFolderWithResult(string sourceFolder, string destinationFolder)
        {
            var result = new ImportResult();
            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException(sourceFolder);

            Directory.CreateDirectory(destinationFolder);

            foreach (var file in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(destinationFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (TryCopy(file, destination))
                    result.ImportedCount++;
                else
                {
                    result.SkippedCount++;
                    result.SkippedFiles.Add(file);
                }
            }
            return result;
        }

        public static int ImportAssetFiles(IEnumerable<string> files, string destinationFolder)
        {
            return ImportAssetFilesWithResult(files, destinationFolder).ImportedCount;
        }

        public static ImportResult ImportAssetFilesWithResult(IEnumerable<string> files, string destinationFolder)
        {
            var result = new ImportResult();
            Directory.CreateDirectory(destinationFolder);
            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                var name = Path.GetFileName(file);
                var destination = Path.Combine(destinationFolder, name);
                if (TryCopy(file, destination))
                    result.ImportedCount++;
                else
                {
                    result.SkippedCount++;
                    result.SkippedFiles.Add(file);
                }
            }
            return result;
        }

        private static bool TryCopy(string source, string destination, int maxAttempts = 5)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    File.Copy(source, destination, true);
                    return true;
                }
                catch (IOException)
                {
                    if (attempt == maxAttempts - 1) return false;
                    Thread.Sleep(200 * (attempt + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == maxAttempts - 1) return false;
                    Thread.Sleep(200 * (attempt + 1));
                }
            }
            return false;
        }
    }
}