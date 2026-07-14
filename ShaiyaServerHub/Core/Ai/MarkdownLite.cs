using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Parser Markdown volutamente leggero (nessuna dipendenza NuGet) per renderizzare le risposte
    /// dell'AI in un RichTextBox — copre quanto i modelli scrivono davvero in questa chat (osservato in
    /// sessione: titoli #, grassetto/corsivo, elenchi puntati/numerati, tabelle |a|b|, codice inline).
    /// Non è un motore CommonMark completo: è tarato sull'output reale, non su ogni caso limite.
    /// </summary>
    public static class MarkdownLite
    {
        private static readonly Regex InlineToken = new Regex(
            @"(\*\*.+?\*\*)|(__.+?__)|(`[^`\n]+?`)|(\*[^*\n]+?\*)|(_[^_\n]+?_)",
            RegexOptions.Compiled);
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex ListItem = new Regex(@"^([-*•]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex ListLine = new Regex(@"^([-*•]|\d+[.)])\s+", RegexOptions.Compiled);
        private static readonly Regex TableSeparator = new Regex(@"^\s*\|?[\s:|-]+\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRule = new Regex(@"^-{3,}$", RegexOptions.Compiled);

        private static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC8, 0x81));
        private static readonly SolidColorBrush BulletBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0xE8, 0xC8));
        private static readonly SolidColorBrush CodeBg = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush TableBg = new SolidColorBrush(Color.FromArgb(0x22, 0xE8, 0xC8, 0x81));

        /// <summary>Aggiunge il testo markdown come blocchi in coda al FlowDocument.</summary>
        public static void AppendBlocks(FlowDocument doc, string markdown)
        {
            foreach (var block in SplitBlocks(markdown))
                doc.Blocks.Add(block);
        }

        /// <summary>Aggiunge un'intestazione turno ("Tu — 10:23:11") + il contenuto come blocchi markdown.</summary>
        public static void AppendTurn(FlowDocument doc, string who, string text, bool isAi)
        {
            var header = new Paragraph(new Run($"{who} — {DateTime.Now:HH:mm:ss}"))
            {
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 2),
                Foreground = isAi ? BulletBrush : AccentBrush
            };
            doc.Blocks.Add(header);
            AppendBlocks(doc, text);
        }

        private static IEnumerable<Block> SplitBlocks(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                var h = Heading.Match(line);
                if (h.Success) { yield return BuildHeading(h.Groups[1].Value.Length, h.Groups[2].Value); i++; continue; }

                if (line.TrimStart().StartsWith("|"))
                {
                    var tableLines = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("|")) { tableLines.Add(lines[i]); i++; }
                    yield return BuildTable(tableLines);
                    continue;
                }

                if (ListLine.IsMatch(line.TrimStart()))
                {
                    var listLines = new List<string>();
                    while (i < lines.Length && ListLine.IsMatch(lines[i].TrimStart())) { listLines.Add(lines[i]); i++; }
                    foreach (var p in BuildList(listLines)) yield return p;
                    continue;
                }

                if (HorizontalRule.IsMatch(line.Trim())) { i++; continue; }

                var paraLines = new List<string>();
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) &&
                       !Heading.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("|") &&
                       !ListLine.IsMatch(lines[i].TrimStart()))
                { paraLines.Add(lines[i]); i++; }
                yield return BuildParagraph(string.Join(" ", paraLines));
            }
        }

        private static Paragraph BuildHeading(int level, string text)
        {
            return new Paragraph(new Run(text.Trim()) { FontWeight = FontWeights.Bold })
            {
                FontSize = Math.Max(12, 19 - level * 1.5),
                Margin = new Thickness(0, 8, 0, 4),
                Foreground = AccentBrush
            };
        }

        private static Paragraph BuildParagraph(string text)
        {
            var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var inline in ParseInlines(text)) p.Inlines.Add(inline);
            return p;
        }

        private static IEnumerable<Paragraph> BuildList(List<string> lines)
        {
            foreach (var raw in lines)
            {
                var m = ListItem.Match(raw.TrimStart());
                string marker = m.Success && char.IsDigit(m.Groups[1].Value[0]) ? m.Groups[1].Value + " " : "• ";
                string content = m.Success ? m.Groups[2].Value : raw;
                var p = new Paragraph { Margin = new Thickness(14, 0, 0, 2) };
                p.Inlines.Add(new Run(marker) { Foreground = BulletBrush });
                foreach (var inline in ParseInlines(content)) p.Inlines.Add(inline);
                yield return p;
            }
        }

        private static Paragraph BuildTable(List<string> lines)
        {
            // Niente Table WPF vero (parsing colonne/allineamento non vale la complessità qui): blocco
            // monospace che preserva la struttura testuale — leggibile, non "bello" ma corretto.
            var p = new Paragraph
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4),
                Background = TableBg
            };
            bool first = true;
            foreach (var raw in lines)
            {
                if (TableSeparator.IsMatch(raw)) continue; // riga |---|---| : rumore puro in monospace
                if (!first) p.Inlines.Add(new LineBreak());
                p.Inlines.Add(new Run(raw.Trim()));
                first = false;
            }
            return p;
        }

        private static IEnumerable<Inline> ParseInlines(string text)
        {
            text = text ?? "";
            int pos = 0;
            foreach (Match m in InlineToken.Matches(text))
            {
                if (m.Index > pos) yield return new Run(text.Substring(pos, m.Index - pos));
                yield return BuildInlineToken(m.Value);
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) yield return new Run(text.Substring(pos));
        }

        private static Inline BuildInlineToken(string token)
        {
            if (token.Length >= 4 && token.StartsWith("**") && token.EndsWith("**"))
                return new Run(token.Substring(2, token.Length - 4)) { FontWeight = FontWeights.Bold };
            if (token.Length >= 4 && token.StartsWith("__") && token.EndsWith("__"))
                return new Run(token.Substring(2, token.Length - 4)) { FontWeight = FontWeights.Bold };
            if (token.Length >= 2 && token.StartsWith("`") && token.EndsWith("`"))
                return new Run(token.Substring(1, token.Length - 2)) { FontFamily = new FontFamily("Consolas"), Background = CodeBg };
            if (token.Length >= 2 && token.StartsWith("*") && token.EndsWith("*"))
                return new Run(token.Substring(1, token.Length - 2)) { FontStyle = FontStyles.Italic };
            if (token.Length >= 2 && token.StartsWith("_") && token.EndsWith("_"))
                return new Run(token.Substring(1, token.Length - 2)) { FontStyle = FontStyles.Italic };
            return new Run(token);
        }
    }
}
