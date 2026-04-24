using System;
using Avalonia.Media;

namespace NanoAgent.Desktop.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? StatusNote = null)
{
    public ChatMessage(string role, string content)
        : this(role, content, DateTimeOffset.Now)
    {
    }

    public ChatMessage(string role, string content, string? statusNote)
        : this(role, content, DateTimeOffset.Now, statusNote)
    {
    }

    public bool HasStatusNote => !string.IsNullOrWhiteSpace(StatusNote);

    public IBrush AvatarBackground => Role switch
    {
        "You" => Brush.Parse("#1D283A"),
        "Tool" => Brush.Parse("#1D2D24"),
        "Plan" => Brush.Parse("#332716"),
        _ => Brush.Parse("#151922")
    };

    public IBrush AvatarBorderBrush => Role switch
    {
        "You" => Brush.Parse("#365A8C"),
        "Tool" => Brush.Parse("#2F6B3A"),
        "Plan" => Brush.Parse("#8A5A13"),
        _ => Brush.Parse("#2B313A")
    };

    public IBrush BubbleBackground => Role switch
    {
        "You" => Brush.Parse("#0F172A"),
        "Tool" => Brush.Parse("#0D1A12"),
        "Plan" => Brush.Parse("#1E1A12"),
        _ => Brush.Parse("#111317")
    };

    public IBrush BubbleBorderBrush => Role switch
    {
        "You" => Brush.Parse("#25324A"),
        "Tool" => Brush.Parse("#22552D"),
        "Plan" => Brush.Parse("#785A18"),
        _ => Brush.Parse("#262B33")
    };

    public IBrush ContentForeground => Role switch
    {
        "Tool" => Brush.Parse("#C7F9D4"),
        "Plan" => Brush.Parse("#FDE68A"),
        _ => Brush.Parse("#E5E7EB")
    };

    public IBrush RoleForeground => Role switch
    {
        "Tool" => Brush.Parse("#86EFAC"),
        "Plan" => Brush.Parse("#FBBF24"),
        _ => Brush.Parse("#9CA3AF")
    };
}
