using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NanoAgent.VS.ToolWindows
{
    /// <summary>
    /// Base class for all chat messages with INotifyPropertyChanged for streaming updates.
    /// </summary>
    public abstract class ChatMessage : INotifyPropertyChanged
    {
        private string _text = string.Empty;

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Internal so derived types and the control can trigger property changed notifications.</summary>
        internal void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class UserMessage : ChatMessage { }
    public sealed class AssistantMessage : ChatMessage { }
    public sealed class ReasoningMessage : ChatMessage { }
    public sealed class ToolMessage : ChatMessage { }
    public sealed class SystemMessage : ChatMessage { }

    /// <summary>
    /// A tool-call status card shown while tools execute.
    /// </summary>
    public sealed class ToolCallStatusMessage : ChatMessage
    {
        private string _toolCallId = string.Empty;
        private string _title = string.Empty;
        private string _kind = string.Empty;
        private string _status = "running";
        private string _rawInput = string.Empty;

        public string ToolCallId
        {
            get => _toolCallId;
            set { _toolCallId = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Kind
        {
            get => _kind;
            set { _kind = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusPrefix)); OnPropertyChanged(nameof(DisplayText)); }
        }

        public string RawInput
        {
            get => _rawInput;
            set { _rawInput = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> ContentLines { get; } = new();

        public string StatusPrefix => Status switch
        {
            "running" => "\u25B6",    // ▶
            "completed" => "\u2713",  // ✓
            "failed" => "\u2717",     // ✗
            _ => "\u25CB"             // ○
        };

        public string DisplayText
        {
            get
            {
                string result = $"{StatusPrefix} {Title}";
                if (ContentLines.Count > 0)
                {
                    result += $"\n{string.Join("\n", ContentLines)}";
                }
                return result;
            }
        }
    }

    /// <summary>
    /// A segment of parsed message text (plain or code block).
    /// </summary>
    public sealed class MessageSegment
    {
        public string Text { get; init; } = string.Empty;
        public bool IsCode { get; init; }
    }

    /// <summary>
    /// Splits message text into plain and code-block segments.
    /// Uses Substring for .NET Framework 4.7.2 compatibility (no System.Range).
    /// </summary>
    public static class MessageParser
    {
        private static readonly Regex CodeBlockRegex = new(
            @"```(\w*)\n?(.*?)```",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public static List<MessageSegment> Parse(string text)
        {
            var segments = new List<MessageSegment>();
            if (string.IsNullOrEmpty(text))
            {
                segments.Add(new MessageSegment { Text = "", IsCode = false });
                return segments;
            }

            int last = 0;
            foreach (Match match in CodeBlockRegex.Matches(text))
            {
                if (match.Index > last)
                {
                    string plain = text.Substring(last, match.Index - last);
                    if (!string.IsNullOrEmpty(plain))
                        segments.Add(new MessageSegment { Text = plain, IsCode = false });
                }

                string code = match.Groups[2].Value;
                segments.Add(new MessageSegment { Text = code, IsCode = true });
                last = match.Index + match.Length;
            }

            if (last < text.Length)
            {
                string remaining = text.Substring(last);
                if (!string.IsNullOrEmpty(remaining))
                    segments.Add(new MessageSegment { Text = remaining, IsCode = false });
            }

            if (segments.Count == 0)
                segments.Add(new MessageSegment { Text = text, IsCode = false });

            return segments;
        }
    }

    /// <summary>
    /// Converts message text string into a list of <see cref="MessageSegment"/> for template rendering.
    /// </summary>
    public sealed class MessageToSegmentsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
                return MessageParser.Parse(text);
            return new List<MessageSegment> { new() { Text = value?.ToString() ?? "", IsCode = false } };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts an available width into a fractional maximum width for message cards.
    /// </summary>
    public sealed class WidthFractionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double width = value is double d ? d : double.NaN;
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
                return double.NaN;

            double fraction = 1.0;
            if (parameter != null)
            {
                _ = double.TryParse(
                    parameter.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out fraction);
            }

            return Math.Max(0, width * fraction);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Selects between code and text DataTemplates based on <see cref="MessageSegment.IsCode"/>.
    /// </summary>
    public sealed class SegmentTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? CodeTemplate { get; set; }
        public DataTemplate? TextTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is MessageSegment seg)
                return seg.IsCode ? CodeTemplate : TextTemplate;
            return TextTemplate;
        }
    }
}
