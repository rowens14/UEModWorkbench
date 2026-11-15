using ICSharpCode.AvalonEdit.Document;

namespace IrrModWorkbench.Services
{
    public static class BracketMatcher
    {
        private static bool IsOpen(char c) => c == '{' || c == '[' || c == '(';
        private static bool IsClose(char c) => c == '}' || c == ']' || c == ')';
        private static char MatchingOpen(char c) => c == '}' ? '{' : c == ']' ? '[' : '(';
        private static char MatchingClose(char c) => c == '{' ? '}' : c == '[' ? ']' : ')';

        /// <summary>
        /// Returns the offset of the matching bracket for the bracket at/just-before
        /// <paramref name="offset"/>, or -1 if none.
        /// </summary>
        public static int FindMatch(TextDocument doc, int offset)
        {
            if (doc == null || doc.TextLength == 0) return -1;

            int pos = -1;
            if (offset < doc.TextLength && IsBracket(doc.GetCharAt(offset)))
                pos = offset;
            else if (offset > 0 && IsBracket(doc.GetCharAt(offset - 1)))
                pos = offset - 1;

            if (pos < 0) return -1;

            char ch = doc.GetCharAt(pos);
            bool isOpen = IsOpen(ch);

            // Scan, skipping strings.
            int depth = 0;
            if (isOpen)
            {
                for (int i = pos + 1; i < doc.TextLength; i++)
                {
                    char c = doc.GetCharAt(i);
                    if (c == '"') { i = SkipString(doc, i); continue; }
                    if (c == ch) depth++;
                    else if (c == MatchingClose(ch))
                    {
                        if (depth == 0) return i;
                        depth--;
                    }
                }
            }
            else
            {
                char open = MatchingOpen(ch);
                for (int i = pos - 1; i >= 0; i--)
                {
                    char c = doc.GetCharAt(i);
                    if (c == '"') { i = SkipStringBackward(doc, i); continue; }
                    if (c == ch) depth++;
                    else if (c == open)
                    {
                        if (depth == 0) return i;
                        depth--;
                    }
                }
            }
            return -1;
        }

        private static bool IsBracket(char c) => IsOpen(c) || IsClose(c);

        private static int SkipString(TextDocument doc, int quoteIndex)
        {
            // quoteIndex points at opening quote; advance past closing quote.
            int i = quoteIndex + 1;
            while (i < doc.TextLength)
            {
                char c = doc.GetCharAt(i);
                if (c == '\\') { i += 2; continue; }
                if (c == '"') return i;
                i++;
            }
            return doc.TextLength - 1;
        }

        private static int SkipStringBackward(TextDocument doc, int quoteIndex)
        {
            // quoteIndex points at a closing quote; walk back to opening quote.
            int i = quoteIndex - 1;
            while (i >= 0)
            {
                char c = doc.GetCharAt(i);
                if (c == '\\') { i -= 2; continue; }
                if (c == '"') return i;
                i--;
            }
            return 0;
        }
    }
}
