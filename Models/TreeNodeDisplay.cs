using System.Windows;

namespace IrrModWorkbench.Models
{
    public class TreeNodeDisplay
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public object? Tag { get; set; }

        public string DisplayText => string.IsNullOrEmpty(Preview)
            ? $"{Name} : {Type}"
            : $"{Name} : {Type} = {Preview}";
    }
}
