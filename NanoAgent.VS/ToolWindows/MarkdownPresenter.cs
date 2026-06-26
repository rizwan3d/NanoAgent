using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NanoAgent.VS.ToolWindows
{
    /// <summary>
    /// Lightweight Markdown → WPF renderer for chat messages. Supports headings, bold/italic,
    /// inline code, links, bullet/numbered lists, blockquotes, horizontal rules, fenced code
    /// blocks (with ```diff rendered as a colored diff), and clickable file/URL autolinks.
    /// File links navigate via the "nanofile" scheme; the host handles Hyperlink.RequestNavigate.
    /// single-pass inline parser, no nested emphasis. Covers virtually all agent output;
    /// upgrade to a full CommonMark lib only if real messages break it.
    /// </summary>
    public sealed class MarkdownPresenter : ContentControl
    {
        public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownPresenter),
            new PropertyMetadata(string.Empty, OnChanged));

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((MarkdownPresenter)d).Rebuild();

        public static Uri FileUri(string path, int line = 0)
            => new("nanofile://open/?p=" + Uri.EscapeDataString(path ?? string.Empty) + "&l=" + line);

        // ── Regex catalog ──
        private static readonly Regex Fence = new(@"^\s*(```|~~~)(.*)$", RegexOptions.Compiled);
        private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex Hr = new(@"^\s*([-*_])\1\1+\s*$", RegexOptions.Compiled);
        private static readonly Regex Bullet = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex Numbered = new(@"^(\s*)(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex Quote = new(@"^\s*>\s?(.*)$", RegexOptions.Compiled);

        private static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex InlineToken = new(
            @"(?<color>\{c:(?<cn>red|green|yellow)\}(?<ct>.+?)\{/c\})" +
            @"|(?<bold>\*\*(?<b>.+?)\*\*|__(?<b2>.+?)__)" +
            @"|(?<italic>\*(?<i>[^*\s].*?)\*|_(?<i2>[^_\s].*?)_)" +
            @"|(?<link>\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)\))",
            RegexOptions.Compiled);
        private static readonly Regex AutoLink = new(
            @"(?<url>https?://[^\s)]+)" +
            @"|(?<path>(?<![\w/\\])[A-Za-z0-9_.\-]+(?:[\\/][A-Za-z0-9_.\-]+)+(?::\d+(?::\d+)?)?)",
            RegexOptions.Compiled);

        private void Rebuild()
        {
            var root = new StackPanel();
            foreach (UIElement block in RenderBlocks(Markdown ?? string.Empty))
            {
                root.Children.Add(block);
            }
            Content = root;
        }

        private static IEnumerable<UIElement> RenderBlocks(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var blocks = new List<UIElement>();
            int i = 0;

            while (i < lines.Length)
            {
                string line = lines[i];

                // Fenced code block
                Match fence = Fence.Match(line);
                if (fence.Success)
                {
                    string lang = fence.Groups[2].Value.Trim();
                    var code = new List<string>();
                    i++;
                    while (i < lines.Length && !Fence.IsMatch(lines[i])) { code.Add(lines[i]); i++; }
                    if (i < lines.Length) i++; // closing fence
                    string body = string.Join("\n", code);
                    blocks.Add(lang.Equals("diff", StringComparison.OrdinalIgnoreCase)
                        ? new DiffPresenter { Diffs = DiffModel.ParseDiffText(body) }
                        : CodeBlock(body, lang));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                if (Hr.IsMatch(line))
                {
                    blocks.Add(new Border { Height = 1, Background = ChatBrushes.Border, Margin = new Thickness(0, 6, 0, 6) });
                    i++;
                    continue;
                }

                Match heading = Heading.Match(line);
                if (heading.Success)
                {
                    blocks.Add(HeadingBlock(heading.Groups[1].Value.Length, heading.Groups[2].Value));
                    i++;
                    continue;
                }

                if (Quote.IsMatch(line))
                {
                    var quoted = new List<string>();
                    while (i < lines.Length && Quote.IsMatch(lines[i])) { quoted.Add(Quote.Match(lines[i]).Groups[1].Value); i++; }
                    blocks.Add(QuoteBlock(string.Join("\n", quoted)));
                    continue;
                }

                if (Bullet.IsMatch(line) || Numbered.IsMatch(line))
                {
                    var items = new List<(int Indent, string Marker, string Content)>();
                    while (i < lines.Length)
                    {
                        Match b = Bullet.Match(lines[i]);
                        Match n = Numbered.Match(lines[i]);
                        if (b.Success) items.Add((b.Groups[1].Value.Length, "•", b.Groups[2].Value));
                        else if (n.Success) items.Add((n.Groups[1].Value.Length, n.Groups[2].Value + ".", n.Groups[3].Value));
                        else break;
                        i++;
                    }
                    blocks.Add(ListBlock(items));
                    continue;
                }

                // Paragraph: gather consecutive plain lines.
                var para = new List<string>();
                while (i < lines.Length
                       && !string.IsNullOrWhiteSpace(lines[i])
                       && !Fence.IsMatch(lines[i])
                       && !Heading.IsMatch(lines[i])
                       && !Hr.IsMatch(lines[i])
                       && !Quote.IsMatch(lines[i])
                       && !Bullet.IsMatch(lines[i])
                       && !Numbered.IsMatch(lines[i]))
                {
                    para.Add(lines[i]);
                    i++;
                }
                blocks.Add(ParagraphBlock(para));
            }

            if (blocks.Count == 0) blocks.Add(ParagraphBlock(new List<string>()));
            return blocks;
        }

        private static TextBlock ParagraphBlock(IReadOnlyList<string> lines)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = ChatBrushes.Text,
                LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 6)
            };
            for (int j = 0; j < lines.Count; j++)
            {
                if (j > 0) tb.Inlines.Add(new LineBreak());
                AddInlines(tb.Inlines, lines[j]);
            }
            SelectableTextBlock.SetIsEnabled(tb, true);
            return tb;
        }

        private static TextBlock HeadingBlock(int level, string content)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = ChatBrushes.Text,
                FontWeight = FontWeights.SemiBold,
                FontSize = level <= 1 ? 18 : level == 2 ? 16 : level == 3 ? 14 : 13,
                Margin = new Thickness(0, level <= 2 ? 8 : 4, 0, 4)
            };
            AddInlines(tb.Inlines, content);
            SelectableTextBlock.SetIsEnabled(tb, true);
            return tb;
        }

        private static UIElement QuoteBlock(string content)
        {
            return new Border
            {
                BorderBrush = ChatBrushes.Border,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 2, 0, 2),
                Margin = new Thickness(0, 2, 0, 6),
                Child = ParagraphBlock(content.Split('\n')).Also(t => { t.Foreground = ChatBrushes.Dim; t.Margin = new Thickness(0); })
            };
        }

        private static UIElement ListBlock(IReadOnlyList<(int Indent, string Marker, string Content)> items)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (indent, marker, content) in items)
            {
                var row = new Grid { Margin = new Thickness(8 + Math.Min(indent, 8) * 2, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = marker + " ",
                    Foreground = ChatBrushes.Dim,
                    Margin = new Thickness(0, 0, 4, 0),
                    MinWidth = 16
                };
                Grid.SetColumn(bullet, 0);
                row.Children.Add(bullet);

                var body = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ChatBrushes.Text, LineHeight = 20 };
                AddInlines(body.Inlines, content);
                SelectableTextBlock.SetIsEnabled(body, true);
                Grid.SetColumn(body, 1);
                row.Children.Add(body);

                panel.Children.Add(row);
            }
            return panel;
        }

        private static UIElement CodeBlock(string code, string lang)
        {
            var border = new Border
            {
                Background = ChatBrushes.CodeBg,
                BorderBrush = ChatBrushes.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 6)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBlock
            {
                Text = code,
                FontFamily = ChatBrushes.Mono,
                FontSize = 12,
                Foreground = ChatBrushes.Code,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            SelectableTextBlock.SetIsEnabled(tb, true);
            Grid.SetColumn(tb, 0);
            grid.Children.Add(tb);

            var copy = ChatUi.IconButton("Copy", string.IsNullOrEmpty(lang) ? "Copy code" : "Copy " + lang);
            copy.VerticalAlignment = VerticalAlignment.Top;
            copy.Click += (_, _) => ChatUi.CopyToClipboard(code);
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);

            border.Child = grid;
            return border;
        }

        // ── Inline parsing ──

        private static void AddInlines(InlineCollection target, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int last = 0;
            foreach (Match m in InlineCode.Matches(text))
            {
                if (m.Index > last) AddFormatted(target, text.Substring(last, m.Index - last));
                target.Add(new Run(m.Groups[1].Value)
                {
                    FontFamily = ChatBrushes.Mono,
                    Background = ChatBrushes.CodeBg,
                    Foreground = ChatBrushes.Code
                });
                last = m.Index + m.Length;
            }
            if (last < text.Length) AddFormatted(target, text.Substring(last));
        }

        private static void AddFormatted(InlineCollection target, string text)
        {
            int last = 0;
            foreach (Match m in InlineToken.Matches(text))
            {
                if (m.Index > last) AddAutoLinked(target, text.Substring(last, m.Index - last));

                if (m.Groups["color"].Success)
                {
                    Brush brush = m.Groups["cn"].Value switch
                    {
                        "green" => ChatBrushes.AddFg,
                        "red" => ChatBrushes.DelFg,
                        _ => ChatBrushes.WarnFg,
                    };
                    target.Add(new Run(m.Groups["ct"].Value) { Foreground = brush });
                }
                else if (m.Groups["bold"].Success)
                {
                    string content = m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value;
                    target.Add(new Bold(new Run(content)));
                }
                else if (m.Groups["italic"].Success)
                {
                    string content = m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value;
                    target.Add(new Italic(new Run(content)));
                }
                else if (m.Groups["link"].Success)
                {
                    target.Add(MakeLink(m.Groups["lt"].Value, m.Groups["lu"].Value));
                }
                last = m.Index + m.Length;
            }
            if (last < text.Length) AddAutoLinked(target, text.Substring(last));
        }

        private static void AddAutoLinked(InlineCollection target, string text)
        {
            int last = 0;
            foreach (Match m in AutoLink.Matches(text))
            {
                if (m.Index > last) target.Add(new Run(text.Substring(last, m.Index - last)));
                if (m.Groups["url"].Success)
                {
                    string url = m.Groups["url"].Value.TrimEnd('.', ',', ';', ':');
                    target.Add(MakeLink(url, url));
                }
                else
                {
                    string token = m.Groups["path"].Value;
                    target.Add(MakeLink(token, token));
                }
                last = m.Index + m.Length;
            }
            if (last < text.Length) target.Add(new Run(text.Substring(last)));
        }

        private static Hyperlink MakeLink(string label, string target)
        {
            Uri uri;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Uri.TryCreate(target, UriKind.Absolute, out uri!);
            }
            else
            {
                // file path with optional :line:col
                string path = target;
                int line = 0;
                Match m = Regex.Match(target, @"^(.*?):(\d+)(?::\d+)?$");
                if (m.Success) { path = m.Groups[1].Value; int.TryParse(m.Groups[2].Value, out line); }
                uri = FileUri(path, line);
            }

            var link = new Hyperlink(new Run(label)) { Foreground = ChatBrushes.Link };
            if (uri != null) link.NavigateUri = uri;
            return link;
        }
    }

    internal static class FluentExtensions
    {
        public static T Also<T>(this T self, Action<T> configure) { configure(self); return self; }
    }
}
