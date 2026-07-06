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

    // When true, this message was produced by ShowToolCalls (a tool call notification
    // shown before execution results). When results arrive, ShowToolResults merges the
    // tool call text into the first result message and removes this message so the
    // call and its output appear as one collapsed block.
    public bool IsToolCallMessage { get; set; }
}
