using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace IrrModWorkbench.Services
{
    /// <summary>
    /// Highlights the bracket under the caret and its matching partner.
    /// </summary>
    public class BracketMatchTransformer : DocumentColorizingTransformer
    {
        public int CaretBracket { get; set; } = -1;
        public int MatchBracket { get; set; } = -1;

        private static readonly Brush HighlightBrush =
            new SolidColorBrush(Color.FromArgb(100, 255, 235, 59));

        protected override void ColorizeLine(DocumentLine line)
        {
            if (CaretBracket < 0 && MatchBracket < 0) return;
            var doc = CurrentContext.Document;
            if (doc == null) return;

            foreach (var off in new[] { CaretBracket, MatchBracket })
            {
                if (off < 0 || off >= doc.TextLength) continue;
                var l = doc.GetLineByOffset(off);
                if (l.LineNumber != line.LineNumber) continue;
                int col = off - l.Offset;
                ChangeLinePart(l.Offset + col, l.Offset + col + 1, element =>
                {
                    element.TextRunProperties.SetBackgroundBrush(HighlightBrush);
                });
            }
        }
    }
}
