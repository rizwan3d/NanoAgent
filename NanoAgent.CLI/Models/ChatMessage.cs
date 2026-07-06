using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public sealed class ChatMessage
{
    public int Id { get; init; }

    public Role Role { get; init; }

    public string Text { get; set; } = string.Empty;

    // When set, the message renders as the "Files modified" table instead of plain text.
    public IReadOnlyList<FileEditSummary>? FileEdits { get; set; }

    // When true, the message is a tool output that can be collapsed/expanded by the user
    // via click or Ctrl+T (same interaction model as thinking/reasoning blocks).
    public bool IsCollapsibleToolMessage { get; set; }
}
