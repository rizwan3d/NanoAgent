namespace NanoAgent.CLI;

public sealed class ChatMessage
{
    public int Id { get; init; }

    public Role Role { get; init; }

    public string Text { get; set; } = string.Empty;
}
