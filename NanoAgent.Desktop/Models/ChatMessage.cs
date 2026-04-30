using Avalonia.Media;

namespace NanoAgent.Desktop.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? StatusNote = null,
    string? WorkspacePath = null)
{
    public ChatMessage(string role, string content)
        : this(role, content, DateTimeOffset.Now)
    {
    }

    public ChatMessage(string role, string content, string? statusNote)
        : this(role, content, DateTimeOffset.Now, statusNote)
    {
    }

    public ChatMessage(string role, string content, string? statusNote, string? workspacePath)
        : this(role, content, DateTimeOffset.Now, statusNote, workspacePath)
    {
    }

    public bool HasStatusNote => !string.IsNullOrWhiteSpace(StatusNote);

    public string AvatarText => Role switch
    {
        "You" => "Y",
        "Tool" => "T",
        "Plan" => "P",
        _ => "N"
    };

    public IBrush AvatarBackground => Role switch
    {
        "You" => Brush.Parse("#1D283A"),
        "Tool" => Brush.Parse("#172033"),
        "Plan" => Brush.Parse("#332716"),
        _ => Brush.Parse("#151922")
    };

    public IBrush AvatarBorderBrush => Role switch
    {
        "You" => Brush.Parse("#365A8C"),
        "Tool" => Brush.Parse("#2F4F7A"),
        "Plan" => Brush.Parse("#8A5A13"),
        _ => Brush.Parse("#2B313A")
    };

    public IBrush BubbleBackground => Role switch
    {
        "You" => Brush.Parse("#0F172A"),
        "Tool" => Brush.Parse("#0D121C"),
        "Plan" => Brush.Parse("#1E1A12"),
        _ => Brush.Parse("#111317")
    };

    public IBrush BubbleBorderBrush => Role switch
    {
        "You" => Brush.Parse("#25324A"),
        "Tool" => Brush.Parse("#22324D"),
        "Plan" => Brush.Parse("#785A18"),
        _ => Brush.Parse("#262B33")
    };

    public IBrush ContentForeground => Role switch
    {
        "Tool" => Brush.Parse("#D7E8FF"),
        "Plan" => Brush.Parse("#FDE68A"),
        _ => Brush.Parse("#E5E7EB")
    };

    public IBrush RoleForeground => Role switch
    {
        "Tool" => Brush.Parse("#93C5FD"),
        "Plan" => Brush.Parse("#FBBF24"),
        _ => Brush.Parse("#9CA3AF")
    };
}
