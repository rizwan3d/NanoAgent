using System.Text.Json;

namespace NanoAgent.Application.Models;

public sealed class ToolExecutionContext
{
    public ToolExecutionContext(
        string toolCallId,
        string toolName,
        JsonElement arguments,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase = ConversationExecutionPhase.Execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(session);

        ToolCallId = toolCallId.Trim();
        ToolName = toolName.Trim();
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool arguments must be a JSON object.",
                nameof(arguments));
        }

        Arguments = arguments;
        ExecutionPhase = executionPhase;
        Session = session;
    }

    public JsonElement Arguments { get; }

    public ConversationExecutionPhase ExecutionPhase { get; }

    public ReplSessionContext Session { get; }

    public string ToolCallId { get; }

    public string ToolName { get; }
}
