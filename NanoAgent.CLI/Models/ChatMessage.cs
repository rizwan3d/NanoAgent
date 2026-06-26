using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public sealed class ChatMessage
{
    public int Id { get; init; }

    public Role Role { get; init; }

    public string Text { get; set; } = string.Empty;

    // When set, the message renders as the "Files modified" table instead of plain text.
    public IReadOnlyList<FileEditSummary>? FileEdits { get; set; }
}
