using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace NanoAgent.VS.ToolWindows
{
    /// <summary>Shared dark-theme brushes matching the VS Code chat palette.</summary>
    internal static class ChatBrushes
    {
        public static readonly Brush Text = Frozen(0xCC, 0xCC, 0xCC);
        public static readonly Brush Dim = Frozen(0x8F, 0x8F, 0x8F);
        public static readonly Brush Code = Frozen(0xD4, 0xD4, 0xD4);
        public static readonly Brush Border = Frozen(0x3C, 0x3C, 0x3C);
        public static readonly Brush Panel = Frozen(0x25, 0x25, 0x26);
        public static readonly Brush CodeBg = Frozen(0x2D, 0x2D, 0x30);
        public static readonly Brush Link = Frozen(0x4D, 0xAA, 0xFC);
        public static readonly Brush AddFg = Frozen(0x9C, 0xDC, 0xAA);
        public static readonly Brush DelFg = Frozen(0xF4, 0x87, 0x71);
        public static readonly Brush MetaFg = Frozen(0x6A, 0x95, 0xD6);
        public static readonly Brush AddBg = new SolidColorBrush(Color.FromArgb(0x33, 0x2E, 0xA0, 0x43)) { };
        public static readonly Brush DelBg = new SolidColorBrush(Color.FromArgb(0x33, 0xF4, 0x87, 0x71)) { };

        public static readonly FontFamily Mono = new("Consolas");

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        static ChatBrushes()
        {
            (AddBg as SolidColorBrush)?.Freeze();
            (DelBg as SolidColorBrush)?.Freeze();
        }
    }

    /// <summary>Renders a parsed set of file diffs as colored, copyable blocks.</summary>
    public sealed class DiffPresenter : ContentControl
    {
        public static readonly DependencyProperty DiffsProperty = DependencyProperty.Register(
            nameof(Diffs), typeof(IReadOnlyList<FileDiff>), typeof(DiffPresenter),
            new PropertyMetadata(null, OnChanged));

        public IReadOnlyList<FileDiff>? Diffs
        {
            get => (IReadOnlyList<FileDiff>?)GetValue(DiffsProperty);
            set => SetValue(DiffsProperty, value);
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((DiffPresenter)d).Rebuild();

        private void Rebuild()
        {
            if (Diffs == null || Diffs.Count == 0) { Content = null; return; }

            var root = new StackPanel();
            foreach (FileDiff file in Diffs)
            {
                root.Children.Add(BuildFile(file));
            }
            Content = root;
        }

        private static UIElement BuildFile(FileDiff file)
        {
            var outer = new Border
            {
                BorderBrush = ChatBrushes.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 4),
                Background = ChatBrushes.CodeBg
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header: path + stats + copy
            var header = new Grid { Margin = new Thickness(8, 4, 6, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var link = new Hyperlink(new Run(file.Path))
            {
                Foreground = ChatBrushes.Link,
                NavigateUri = MarkdownPresenter.FileUri(file.Path)
            };
            pathBlock.Inlines.Add(link);
            pathBlock.FontFamily = ChatBrushes.Mono;
            pathBlock.FontSize = 11;
            Grid.SetColumn(pathBlock, 0);
            header.Children.Add(pathBlock);

            var stats = new TextBlock
            {
                Text = $"+{file.Added} -{file.Removed}",
                FontFamily = ChatBrushes.Mono,
                FontSize = 11,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ChatBrushes.Dim
            };
            Grid.SetColumn(stats, 1);
            header.Children.Add(stats);

            var copy = ChatUi.IconButton("Copy", "Copy diff");
            copy.Click += (_, _) => ChatUi.CopyToClipboard(file.ToClipboardText());
            Grid.SetColumn(copy, 2);
            header.Children.Add(copy);

            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Lines
            var lines = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (DiffLine line in file.Lines)
            {
                lines.Children.Add(BuildLine(line));
            }
            Grid.SetRow(lines, 1);
            grid.Children.Add(lines);

            outer.Child = grid;
            return outer;
        }

        private static UIElement BuildLine(DiffLine line)
        {
            (Brush bg, Brush fg, string gutter) = line.Type switch
            {
                DiffLineType.Add => (ChatBrushes.AddBg, ChatBrushes.AddFg, "+"),
                DiffLineType.Del => (ChatBrushes.DelBg, ChatBrushes.DelFg, "-"),
                DiffLineType.Meta => (Brushes.Transparent, ChatBrushes.MetaFg, " "),
                _ => (Brushes.Transparent, ChatBrushes.Code, " ")
            };

            return new Border
            {
                Background = bg,
                Padding = new Thickness(8, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = (line.Type == DiffLineType.Meta ? string.Empty : gutter) + line.Text,
                    FontFamily = ChatBrushes.Mono,
                    FontSize = 12,
                    Foreground = fg,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }
    }

    /// <summary>
    /// Renders the body of a tool-call card: a colored diff when the input is an edit/patch,
    /// otherwise the captured output lines, plus a collapsible raw-arguments view.
    /// </summary>
    public sealed class ToolCallContentPresenter : ContentControl
    {
        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message), typeof(ToolCallStatusMessage), typeof(ToolCallContentPresenter),
            new PropertyMetadata(null, OnChanged));

        public ToolCallStatusMessage? Message
        {
            get => (ToolCallStatusMessage?)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public ToolCallContentPresenter()
        {
            // Rebuild when streaming content/status changes.
            DataContextChanged += (_, _) => { };
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (ToolCallContentPresenter)d;
            if (e.OldValue is ToolCallStatusMessage oldMsg)
            {
                oldMsg.ContentLines.CollectionChanged -= self.OnContentChanged;
                oldMsg.PropertyChanged -= self.OnMessagePropertyChanged;
            }
            if (e.NewValue is ToolCallStatusMessage newMsg)
            {
                newMsg.ContentLines.CollectionChanged += self.OnContentChanged;
                newMsg.PropertyChanged += self.OnMessagePropertyChanged;
            }
            self.Rebuild();
        }

        private void OnContentChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Rebuild();

        private void OnMessagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ToolCallStatusMessage.RawInput) or nameof(ToolCallStatusMessage.Status))
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            ToolCallStatusMessage? msg = Message;
            if (msg == null) { Content = null; return; }

            var panel = new StackPanel();

            List<FileDiff>? diffs = DiffModel.Build(msg.Kind, msg.Title, msg.RawInput);
            if (diffs != null)
            {
                panel.Children.Add(new DiffPresenter { Diffs = diffs });
            }
            else if (msg.ContentLines.Count > 0)
            {
                panel.Children.Add(BuildOutput(string.Join("\n", msg.ContentLines)));
            }
            else if (msg.Status == "running")
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Waiting for output…",
                    Foreground = ChatBrushes.Dim,
                    FontStyle = FontStyles.Italic,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // Collapsible raw arguments.
            if (!string.IsNullOrWhiteSpace(msg.RawInput) && msg.RawInput != "{}")
            {
                var args = new Expander
                {
                    Header = new TextBlock { Text = "Arguments", Foreground = ChatBrushes.Dim, FontSize = 11 },
                    Margin = new Thickness(0, 4, 0, 0),
                    Content = BuildOutput(PrettyJson(msg.RawInput))
                };
                panel.Children.Add(args);
            }

            Content = panel;
        }

        private static UIElement BuildOutput(string text)
        {
            var border = new Border
            {
                Background = Frozen(0x1E, 0x1E, 0x1E),
                BorderBrush = ChatBrushes.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 2, 0, 2)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBlock
            {
                Text = text,
                FontFamily = ChatBrushes.Mono,
                FontSize = 12,
                Foreground = ChatBrushes.Code,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(tb, 0);
            grid.Children.Add(tb);

            var copy = ChatUi.IconButton("Copy", "Copy output");
            copy.VerticalAlignment = VerticalAlignment.Top;
            copy.Click += (_, _) => ChatUi.CopyToClipboard(text);
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);

            border.Child = grid;
            return border;
        }

        private static string PrettyJson(string raw)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch { return raw; }
        }

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Small UI helpers shared across presenters.</summary>
    internal static class ChatUi
    {
        public static Button IconButton(string content, string tooltip)
        {
            return new Button
            {
                Content = content,
                ToolTip = tooltip,
                FontSize = 11,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(2, 0, 0, 0),
                Background = ChatBrushes.Panel,
                Foreground = ChatBrushes.Dim,
                BorderBrush = ChatBrushes.Border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        public static void CopyToClipboard(string text)
        {
            try { Clipboard.SetText(text ?? string.Empty); }
            catch { /* clipboard busy; ignore */ }
        }
    }
}
