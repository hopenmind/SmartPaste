using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartPaste
{
    /// <summary>
    /// Converts an HTML fragment to Markdown for markdown-first editors. Ports the import
    /// rules of Hope 'n Mind's mdall (its "any format → Markdown" pivot) into C#: headings,
    /// emphasis, inline/fenced code, links, images, lists (nested), blockquotes, rules and
    /// simple tables. Unknown tags are dropped and their text kept. Any failure falls back to
    /// tag-stripped, entity-decoded text so a paste is never lost.
    /// </summary>
    public static class HtmlToMarkdown
    {
        private static readonly Regex TagRegex = new(
            @"<(?<close>/?)(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)/?>",
            RegexOptions.Compiled);

        public static string Convert(string? htmlFragment)
        {
            if (string.IsNullOrWhiteSpace(htmlFragment)) return "";
            try { return new Converter().Run(htmlFragment!); }
            catch { return WebUtility.HtmlDecode(Regex.Replace(htmlFragment, "<[^>]+>", " ")).Trim(); }
        }

        private static string? Attr(string attrs, string name)
        {
            var m = Regex.Match(attrs, @"(?<![\w-])" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            return WebUtility.HtmlDecode(v);
        }

        private sealed class Converter
        {
            private readonly StringBuilder _sb = new(1024);
            private readonly Stack<(bool ordered, int count)> _lists = new();
            private readonly Stack<string> _hrefs = new();
            private bool _inPre;

            public string Run(string html)
            {
                // Parse tables out first, splice them back as ready-made Markdown blocks.
                html = Regex.Replace(html, @"<table\b[^>]*>(.*?)</table>",
                    m => "\n\n" + TableToMarkdown(m.Groups[1].Value) + "\n\n",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                int last = 0;
                for (var m = TagRegex.Match(html); m.Success; m = m.NextMatch())
                {
                    if (m.Index > last) Text(html.Substring(last, m.Index - last));
                    last = m.Index + m.Length;
                    Tag(m.Groups["close"].Value == "/", m.Groups["name"].Value.ToLowerInvariant(), m.Groups["attrs"].Value);
                }
                if (last < html.Length) Text(html.Substring(last));

                return Regex.Replace(_sb.ToString(), @"\n{3,}", "\n\n").Trim();
            }

            private void Text(string raw)
            {
                if (_inPre) { _sb.Append(WebUtility.HtmlDecode(raw)); return; }
                string s = WebUtility.HtmlDecode(Regex.Replace(raw, @"\s+", " "));
                if (s.Length == 0) return;
                // trim a leading space right after a newline
                if (_sb.Length > 0 && _sb[_sb.Length - 1] == '\n') s = s.TrimStart();
                _sb.Append(s);
            }

            private void Tag(bool close, string name, string attrs)
            {
                switch (name)
                {
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        if (!close) { Block(); _sb.Append(new string('#', name[1] - '0')).Append(' '); } else Block();
                        break;
                    case "p": case "div": case "section": case "article": Block(); break;
                    case "br": _sb.Append("  \n"); break;
                    case "hr": Block(); _sb.Append("---"); Block(); break;

                    case "strong": case "b": _sb.Append("**"); break;
                    case "em": case "i": _sb.Append('*'); break;
                    case "del": case "s": case "strike": _sb.Append("~~"); break;

                    case "code":
                        if (!_inPre) _sb.Append('`');
                        break;
                    case "pre":
                        if (!close) { Block(); _sb.Append("```\n"); _inPre = true; }
                        else { _inPre = false; if (_sb.Length > 0 && _sb[_sb.Length - 1] != '\n') _sb.Append('\n'); _sb.Append("```"); Block(); }
                        break;
                    case "blockquote":
                        if (!close) { Block(); _sb.Append("> "); } else Block();
                        break;

                    case "a":
                        if (!close) { _hrefs.Push(Attr(attrs, "href") ?? ""); _sb.Append('['); }
                        else { string href = _hrefs.Count > 0 ? _hrefs.Pop() : ""; _sb.Append("](").Append(href).Append(')'); }
                        break;
                    case "img":
                        string alt = Attr(attrs, "alt") ?? "";
                        string src = Attr(attrs, "src") ?? "";
                        _sb.Append("![").Append(alt).Append("](").Append(src).Append(')');
                        break;

                    case "ul": case "ol":
                        if (!close) _lists.Push((name == "ol", 0));
                        else if (_lists.Count > 0) { _lists.Pop(); if (_lists.Count == 0) Block(); }
                        break;
                    case "li":
                        if (!close)
                        {
                            if (_sb.Length > 0 && _sb[_sb.Length - 1] != '\n') _sb.Append('\n');
                            int depth = Math.Max(0, _lists.Count - 1);
                            _sb.Append(new string(' ', depth * 2));
                            if (_lists.Count > 0)
                            {
                                var top = _lists.Pop();
                                if (top.ordered) { top.count++; _sb.Append(top.count).Append(". "); }
                                else _sb.Append("- ");
                                _lists.Push(top);
                            }
                            else _sb.Append("- ");
                        }
                        break;
                }
            }

            /// <summary>Ensures the output is at a fresh block boundary (blank line), never at the very start.</summary>
            private void Block()
            {
                if (_sb.Length == 0) return;
                int nl = 0;
                for (int i = _sb.Length - 1; i >= 0 && _sb[i] == '\n'; i--) nl++;
                while (nl++ < 2) _sb.Append('\n');
            }
        }

        /// <summary>Converts the inner HTML of a &lt;table&gt; into a GitHub pipe table.</summary>
        private static string TableToMarkdown(string tableInner)
        {
            var rows = new List<List<string>>();
            foreach (Match tr in Regex.Matches(tableInner, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var cells = new List<string>();
                foreach (Match td in Regex.Matches(tr.Groups[1].Value, @"<(t[dh])\b[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string cell = WebUtility.HtmlDecode(Regex.Replace(td.Groups[2].Value, "<[^>]+>", " "));
                    cells.Add(Regex.Replace(cell, @"\s+", " ").Trim().Replace("|", "\\|"));
                }
                if (cells.Count > 0) rows.Add(cells);
            }
            if (rows.Count == 0) return "";

            int cols = 0;
            foreach (var r in rows) cols = Math.Max(cols, r.Count);
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append("| ");
                for (int c = 0; c < cols; c++) sb.Append(c < rows[i].Count ? rows[i][c] : "").Append(" | ");
                sb.Append('\n');
                if (i == 0)
                {
                    sb.Append("| ");
                    for (int c = 0; c < cols; c++) sb.Append("--- | ");
                    sb.Append('\n');
                }
            }
            return sb.ToString().TrimEnd('\n');
        }
    }
}
