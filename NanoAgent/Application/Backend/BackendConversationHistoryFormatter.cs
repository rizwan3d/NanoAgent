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

            AddAssistantMessage(messages, turn);
        }

        return messages;
    }

    private static void AddAssistantMessage(
        List<BackendConversationMessage> messages,
        ConversationSectionTurn turn)
    {
        if (!string.IsNullOrWhiteSpace(turn.AssistantResponse))
        {
            messages.Add(new BackendConversationMessage(
                "assistant",
                turn.AssistantResponse.Trim(),
                NormalizeOptionalText(turn.AssistantReasoningContent),
                NormalizeOptionalText(turn.AssistantReasoningDetailsJson)));
        }
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

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
