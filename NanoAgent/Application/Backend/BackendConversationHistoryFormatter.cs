using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Backend;

internal static class BackendConversationHistoryFormatter
{
    private static readonly IToolOutputFormatter DefaultToolOutputFormatter = new ToolOutputFormatter();

    public static IReadOnlyList<BackendConversationMessage> Create(
        ReplSessionContext session,
        IToolOutputFormatter? toolOutputFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        IToolOutputFormatter formatter = toolOutputFormatter ?? DefaultToolOutputFormatter;
        List<BackendConversationMessage> messages = [];

        foreach (ConversationSectionTurn turn in session.ConversationTurns)
        {
            AddMessage(messages, "user", turn.UserInput);

            if (turn.ToolOutputMessages.Count > 0)
            {
                foreach (string toolOutput in turn.ToolOutputMessages)
                {
                    AddMessage(messages, "tool", toolOutput);
                }
            }
            else
            {
                foreach (ConversationToolCall toolCall in turn.ToolCalls)
                {
                    AddMessage(messages, "tool", formatter.FormatCallPreview(toolCall));
                }
            }

            AddMessage(messages, "assistant", turn.AssistantResponse);
        }

        return messages;
    }

    private static void AddMessage(
        List<BackendConversationMessage> messages,
        string role,
        string? content)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            messages.Add(new BackendConversationMessage(role, content.Trim()));
        }
    }
}
