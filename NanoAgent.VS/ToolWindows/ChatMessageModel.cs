using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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

        internal void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class UserMessage : ChatMessage { }
    public sealed class AssistantMessage : ChatMessage { }
    public sealed class ToolMessage : ChatMessage { }
    public sealed class SystemMessage : ChatMessage { }

    /// <summary>Collapsible reasoning/thinking block.</summary>
    public sealed class ReasoningMessage : ChatMessage
    {
        public int LineCount
        {
            get
            {
                if (string.IsNullOrEmpty(Text)) return 0;
                int n = 1;
                foreach (char c in Text) if (c == '\n') n++;
                return n;
            }
        }

        public string Summary => $"Thinking ({LineCount} line{(LineCount == 1 ? "" : "s")})";

        internal void NotifyDerived()
        {
            OnPropertyChanged(nameof(LineCount));
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>A tool-call status card shown while tools execute.</summary>
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
            set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderText)); }
        }

        public string Kind
        {
            get => _kind;
            set { _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(MetaText)); }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusPrefix));
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(MetaText));
            }
        }

        public string RawInput
        {
            get => _rawInput;
            set { _rawInput = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> ContentLines { get; } = new();

        public string StatusPrefix => Status switch
        {
            "running" => "▶",    // ▶
            "completed" => "✓",  // ✓
            "failed" => "✗",     // ✗
            _ => "○"             // ○
        };

        public string HeaderText => $"{StatusPrefix}  {Title}";

        public string MetaText
        {
            get
            {
                string meta = Kind;
                if (!string.IsNullOrEmpty(Status) && Status != "running")
                {
                    meta = string.IsNullOrEmpty(meta) ? Status : $"{meta} · {Status}";
                }
                return meta;
            }
        }
    }

    /// <summary>Converts an available width into a fractional maximum width for message cards.</summary>
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
                _ = double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction);
            }

            return Math.Max(0, width * fraction);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
