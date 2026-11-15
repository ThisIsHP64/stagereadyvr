using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StageReadyVR.Notes
{
    /// <summary>Pure text handling for held notes, kept free of Unity objects so it can be tested.</summary>
    public static class NotesText
    {
        // a line made only of three or more dashes or equals signs marks a card break
        static readonly Regex Separator = new Regex(@"^\s*(-{3,}|={3,})\s*$", RegexOptions.Compiled);
        static readonly Regex BlankLines = new Regex(@"\n[ \t]*\n", RegexOptions.Compiled);

        /// <summary>
        /// Splits index card text into cards. Explicit separator lines win, so a card may
        /// contain blank lines; without any, each blank line starts a new card.
        /// Bullets written as - or * become a round bullet. Never returns an empty list.
        /// </summary>
        public static List<string> SplitCards(string text)
        {
            string normalized = Normalize(text);
            var cards = new List<string>();

            string[] lines = normalized.Split('\n');
            bool hasSeparator = Array.Exists(lines, l => Separator.IsMatch(l));

            if (hasSeparator)
            {
                var current = new StringBuilder();
                foreach (string line in lines)
                {
                    if (Separator.IsMatch(line))
                    {
                        AddCard(cards, current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(line).Append('\n');
                    }
                }
                AddCard(cards, current.ToString());
            }
            else
            {
                foreach (string block in BlankLines.Split(normalized)) AddCard(cards, block);
            }

            if (cards.Count == 0) cards.Add(string.Empty);
            return cards;
        }

        /// <summary>Line endings unified and tabs widened, so layout does not depend on the editor used.</summary>
        public static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
        }

        static void AddCard(List<string> cards, string raw)
        {
            string trimmed = raw.Trim('\n', ' ');
            if (trimmed.Length == 0) return;

            var sb = new StringBuilder();
            foreach (string line in trimmed.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                string t = line.Trim();
                bool bullet = t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("•");
                sb.Append(bullet ? "• " + t.TrimStart('-', '*', '•', ' ') : t);
            }
            cards.Add(sb.ToString());
        }
    }
}
