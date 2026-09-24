using System.Text;
using System.Collections.Generic;

namespace Blocks.Cli
{
    /// <summary>
    /// Renders the plain text the bb_* commands return, in the Unity CLI's own house style: a spaced-caps
    /// heading, a horizontal rule as the only separator, and two-column rows indented under it. No boxes.
    ///
    /// Matching the CLI matters because these commands print into the same terminal session as `unity shell`
    /// and `unity help`, one after another. Output in a different visual language reads as coming from a
    /// different tool.
    ///
    /// Commands return a pre-rendered string rather than a structured object because the CLI prints a result
    /// straight to the terminal — there is no client-side formatter to hand a table to.
    ///
    /// Returning a bare string specifically, rather than an object with a text field in it, is what keeps
    /// this readable. The CLI prints a string result as-is, newlines and all; an object result it folds into
    /// one row of a summary table, which turns every line break into a literal \n. That is also why the
    /// commands are run without --json: the envelope escapes the layout back into one long line.
    /// </summary>
    static class Layout
    {
        /// <summary>Left margin on every line, matching the CLI's own output.</summary>
        const string k_Indent = "  ";

        /// <summary>
        /// Columns available after the indent. Text wraps here rather than being truncated.
        ///
        /// It is a fixed number because a command cannot discover the terminal's width: it runs inside the
        /// Editor and hands back a string. The CLI's own splash does adapt, but it is rendering locally. So
        /// this is set narrow enough to survive a small terminal instead.
        /// </summary>
        const int k_Width = 74;

        /// <summary>
        /// Length of the horizontal rule. Deliberately shorter than <see cref="k_Width"/> — the CLI's rule is
        /// shorter than its content too, which keeps the separator from reading as a table edge.
        /// </summary>
        const int k_RuleWidth = 63;

        /// <summary>Blank columns between the label column and the value column.</summary>
        const int k_LabelGap = 4;

        /// <summary>Narrowest the label column gets, so short-labelled blocks still line up with long ones.</summary>
        const int k_MinLabelWidth = 14;

        #region Headings

        /// <summary>
        /// The Building Blocks heading: the wordmark as spaced caps, the caption after it, then a rule.
        ///
        /// Spaced caps rather than block-letter art because that is what the CLI does with "C  L  I", and
        /// because this prints on every listing — a ten-row logo would bury the answer underneath it.
        /// </summary>
        /// <param name="caption">Text after the wordmark, e.g. "3 commands over the Unity CLI".</param>
        public static string Heading(string caption)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.Append(k_Indent).Append(Spaced("BUILDING")).Append("   ").Append(Spaced("BLOCKS"));
            if (!string.IsNullOrWhiteSpace(caption)) sb.Append("  ·  ").Append(caption);
            sb.AppendLine();
            sb.Append(Rule());
            return sb.ToString();
        }

        /// <summary>A section heading: one line of text, then a rule.</summary>
        public static string Section(string text)
        {
            var sb = new StringBuilder();
            sb.Append(k_Indent).AppendLine(text);
            sb.Append(Rule());
            return sb.ToString();
        }

        /// <summary>The horizontal rule the CLI uses to separate blocks.</summary>
        public static string Rule()
        {
            var sb = new StringBuilder();
            sb.Append(k_Indent).Append('─', k_RuleWidth).AppendLine();
            return sb.ToString();
        }

        #endregion

        #region Body

        /// <summary>A paragraph at the left margin, wrapped. For prose that has no label beside it.</summary>
        public static string Text(string text)
        {
            var sb = new StringBuilder();
            List<string> lines = Wrap(text, k_Width);
            for (int i = 0; i < lines.Count; i++)
                sb.Append(k_Indent).AppendLine(lines[i]);
            return sb.ToString();
        }

        /// <summary>
        /// Label/value rows in two columns. A value too long for its column wraps and hangs under itself
        /// rather than being clipped, so nothing a command reports is ever lost to the layout.
        /// </summary>
        /// <param name="labels">Left column. An empty label continues the row above it.</param>
        /// <param name="values">Right column, index-matched to <paramref name="labels"/>.</param>
        /// <param name="spaced">Puts a blank line between rows, for rows that are paragraphs rather than fields.</param>
        public static string Rows(IReadOnlyList<string> labels, IReadOnlyList<string> values, bool spaced = false)
        {
            int labelWidth = k_MinLabelWidth;
            for (int i = 0; i < labels.Count; i++)
                labelWidth = System.Math.Max(labelWidth, Length(labels[i]));

            int column = labelWidth + k_LabelGap;
            int valueWidth = System.Math.Max(20, k_Width - column);

            var sb = new StringBuilder();
            for (int i = 0; i < labels.Count; i++)
            {
                if (spaced && i > 0) sb.AppendLine();

                string label = labels[i] ?? string.Empty;
                List<string> lines = Wrap(i < values.Count ? values[i] : string.Empty, valueWidth);

                for (int line = 0; line < lines.Count; line++)
                {
                    sb.Append(k_Indent);
                    if (line == 0) sb.Append(label).Append(' ', column - Length(label));
                    else sb.Append(' ', column);
                    sb.AppendLine(lines[line]);
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Strings

        /// <summary>"BLOCKS" as "B L O C K S", the CLI's treatment for a small caps label.</summary>
        public static string Spaced(string word)
        {
            if (string.IsNullOrEmpty(word)) return string.Empty;

            var sb = new StringBuilder(word.Length * 2);
            for (int i = 0; i < word.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(word[i]);
            }
            return sb.ToString();
        }

        /// <summary>Joins values with ", ", or returns <paramref name="empty"/> when there are none.</summary>
        public static string Join(IReadOnlyList<string> values, string empty = "none")
        {
            if (values == null || values.Count == 0) return empty;

            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(values[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Breaks text into lines no wider than <paramref name="width"/>, on spaces. A single word longer
        /// than the column is left over-long rather than split, because the words that hit this are type
        /// names and file paths, and a broken one cannot be copied.
        /// </summary>
        static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            string[] words = text.Split(' ');
            var line = new StringBuilder();

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length == 0) continue;

                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }

            if (line.Length > 0) lines.Add(line.ToString());
            if (lines.Count == 0) lines.Add(string.Empty);
            return lines;
        }

        /// <summary>
        /// Printed width of a string. Every character these commands emit is single-width, so this is the
        /// length — named for what the call sites mean by it.
        /// </summary>
        static int Length(string s) => s == null ? 0 : s.Length;

        #endregion
    }
}
