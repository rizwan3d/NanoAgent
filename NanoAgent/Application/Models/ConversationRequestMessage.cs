namespace NanoAgent.Application.Models;

public sealed class ConversationRequestMessage
{
    private const string AssistantRole = "assistant";
    private const string ToolRole = "tool";
    private const string UserRole = "user";

    private ConversationRequestMessage(
        string role,
        string? content,
        string? toolCallId,
        IReadOnlyList<ConversationToolCall>? toolCalls)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        string normalizedRole = role.Trim();
        IReadOnlyList<ConversationToolCall> normalizedToolCalls = toolCalls is null
            ? []
            : toolCalls
                .Where(static toolCall => toolCall is not null)
                .ToArray();

        switch (normalizedRole)
        {
            case UserRole:
                ArgumentException.ThrowIfNullOrWhiteSpace(content);
                EnsureNoToolMetadata(toolCallId, normalizedToolCalls, normalizedRole);
                break;

            case AssistantRole:
                if (string.IsNullOrWhiteSpace(content) && normalizedToolCalls.Count == 0)
                {
                    throw new ArgumentException(
                        "Assistant messages must include content or at least one tool call.",
                        nameof(content));
                }

                if (!string.IsNullOrWhiteSpace(toolCallId))
                {
                    throw new ArgumentException(
                        "Assistant messages cannot include a tool call id.",
                        nameof(toolCallId));
                }

                break;

            case ToolRole:
                ArgumentException.ThrowIfNullOrWhiteSpace(content);
                ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
                if (normalizedToolCalls.Count > 0)
                {
                    throw new ArgumentException(
                        "Tool messages cannot include assistant tool call definitions.",
                        nameof(toolCalls));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(role),
                    normalizedRole,
                    "Unsupported conversation message role.");
        }

        Role = normalizedRole;
        Content = string.IsNullOrWhiteSpace(content)
            ? null
            : content.Trim();
        ToolCallId = string.IsNullOrWhiteSpace(toolCallId)
            ? null
            : toolCallId.Trim();
        ToolCalls = normalizedToolCalls;
    }

    public string? Content { get; }

    public string Role { get; }

    public string? ToolCallId { get; }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; }

    public static ConversationRequestMessage AssistantMessage(string content)
    {
        return new ConversationRequestMessage(AssistantRole, content, null, null);
    }

    public static ConversationRequestMessage AssistantToolCalls(
        IReadOnlyList<ConversationToolCall> toolCalls,
        string? content = null)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        return new ConversationRequestMessage(AssistantRole, content, null, toolCalls);
    }

    public static ConversationRequestMessage ToolResult(
        string toolCallId,
        string content)
    {
        return new ConversationRequestMessage(ToolRole, content, toolCallId, null);
    }

    public static ConversationRequestMessage User(string content)
    {
        return new ConversationRequestMessage(UserRole, content, null, null);
    }

    private static void EnsureNoToolMetadata(
        string? toolCallId,
        IReadOnlyList<ConversationToolCall> toolCalls,
        string role)
    {
        if (!string.IsNullOrWhiteSpace(toolCallId))
        {
            throw new ArgumentException(
                $"{role} messages cannot include a tool call id.",
                nameof(toolCallId));
        }

        if (toolCalls.Count > 0)
        {
            throw new ArgumentException(
                $"{role} messages cannot include assistant tool call definitions.",
                nameof(toolCalls));
        }
    }
}
