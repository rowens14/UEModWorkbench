using System.IO;

namespace IrrModWorkbench.Models
{
    public class AssetBrowserItem
    {
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime Modified { get; set; }
        public string Status { get; set; } = "Source";

        public string SizeDisplay => SizeBytes < 1024 ? $"{SizeBytes} B" : SizeBytes < 1024 * 1024 ? $"{SizeBytes / 1024.0:F1} KB" : $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
    }
}
