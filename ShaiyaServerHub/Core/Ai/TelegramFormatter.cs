using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaiyaServerHub.Core.Ai
{
    /// <summary>
    /// Converte il markdown leggero prodotto dall'LLM (stesso stile di <see cref="MarkdownLite"/>, usato
    /// per il RichTextBox della chat) in HTML per Telegram (<c>parse_mode=HTML</c>). Bug reale osservato:
    /// il testo del digest automatico arrivava su Telegram con `**`/`*`/`#` letterali — MarkdownV2 di
    /// Telegram richiede l'escape di 18 caratteri speciali (`_*[]()~>#+-=|{}.!`) fuori dai code block, un
    /// singolo carattere non escapato in prosa italiana normale (punti, trattini, parentesi ovunque) fa
    /// fallire l'INTERO messaggio — verificato via ricerca web, fonti multiple confermano HTML più sicuro
    /// (solo `&amp;`/`&lt;`/`&gt;` da escapare). Nessuna dipendenza WPF, a differenza di MarkdownLite.
    /// </summary>
    public static class TelegramFormatter
    {
        private static readonly Regex InlineToken = new Regex(
            @"(\*\*.+?\*\*)|(__.+?__)|(`[^`\n]+?`)|(\*[^*\n]+?\*)|(_[^_\n]+?_)",
            RegexOptions.Compiled);
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex ListLine = new Regex(@"^([-*•]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex TableSeparator = new Regex(@"^\s*\|?[\s:|-]+\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRule = new Regex(@"^-{3,}$", RegexOptions.Compiled);

        /// <summary>
        /// Testo pieno di markdown/prosa → HTML Telegram-safe. Nessun troncamento qui: i chiamanti già
        /// tagliano il markdown grezzo a una lunghezza ragionevole (es. 3500 char) PRIMA di questa
        /// conversione — la crescita dovuta ai tag/entity è marginale, resta sotto il limite Telegram
        /// (4096) nell'uso reale osservato.
        /// </summary>
        public static string ToTelegramHtml(string markdown)
        {
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) { sb.Append('\n'); i++; continue; }

                var h = Heading.Match(line);
                if (h.Success) { sb.Append("<b>").Append(EscapeInline(h.Groups[2].Value.Trim())).Append("</b>\n"); i++; continue; }

                if (line.TrimStart().StartsWith("|"))
                {
                    var tableLines = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("|")) { tableLines.Add(lines[i]); i++; }
                    sb.Append("<pre>");
                    bool first = true;
                    foreach (var raw in tableLines)
                    {
                        if (TableSeparator.IsMatch(raw)) continue;
                        if (!first) sb.Append('\n');
                        sb.Append(HtmlEscape(raw.Trim()));
                        first = false;
                    }
                    sb.Append("</pre>\n");
                    continue;
                }

                var lm = ListLine.Match(line.TrimStart());
                if (lm.Success)
                {
                    string marker = char.IsDigit(lm.Groups[1].Value[0]) ? lm.Groups[1].Value + " " : "• ";
                    sb.Append(marker).Append(EscapeInline(lm.Groups[2].Value)).Append('\n');
                    i++; continue;
                }

                if (HorizontalRule.IsMatch(line.Trim())) { i++; continue; }

                sb.Append(EscapeInline(line)).Append('\n');
                i++;
            }
            return sb.ToString().TrimEnd('\n');
        }

        // Escapa il testo letterale come HTML, ma converte i token markdown (**bold**, *italic*, `code`)
        // nei rispettivi tag Telegram — l'escape va fatto SOLO sul contenuto letterale, mai sui tag stessi.
        private static string EscapeInline(string text)
        {
            text = text ?? "";
            var sb = new StringBuilder();
            int pos = 0;
            foreach (Match m in InlineToken.Matches(text))
            {
                if (m.Index > pos) sb.Append(HtmlEscape(text.Substring(pos, m.Index - pos)));
                sb.Append(TokenToHtml(m.Value));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) sb.Append(HtmlEscape(text.Substring(pos)));
            return sb.ToString();
        }

        private static string TokenToHtml(string token)
        {
            if (token.Length >= 4 && token.StartsWith("**") && token.EndsWith("**"))
                return "<b>" + HtmlEscape(token.Substring(2, token.Length - 4)) + "</b>";
            if (token.Length >= 4 && token.StartsWith("__") && token.EndsWith("__"))
                return "<b>" + HtmlEscape(token.Substring(2, token.Length - 4)) + "</b>";
            if (token.Length >= 2 && token.StartsWith("`") && token.EndsWith("`"))
                return "<code>" + HtmlEscape(token.Substring(1, token.Length - 2)) + "</code>";
            if (token.Length >= 2 && token.StartsWith("*") && token.EndsWith("*"))
                return "<i>" + HtmlEscape(token.Substring(1, token.Length - 2)) + "</i>";
            if (token.Length >= 2 && token.StartsWith("_") && token.EndsWith("_"))
                return "<i>" + HtmlEscape(token.Substring(1, token.Length - 2)) + "</i>";
            return HtmlEscape(token);
        }

        private static string HtmlEscape(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
