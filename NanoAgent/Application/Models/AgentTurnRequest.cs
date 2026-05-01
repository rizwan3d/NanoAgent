using NanoAgent.Application.Abstractions;

namespace NanoAgent.Application.Models;

public sealed class AgentTurnRequest
{
    public AgentTurnRequest(
        ReplSessionContext session,
        string userInput,
        IConversationProgressSink progressSink,
        IReadOnlyList<ConversationAttachment>? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        ArgumentNullException.ThrowIfNull(progressSink);

        Session = session;
        UserInput = userInput.Trim();
        ProgressSink = progressSink;
        Attachments = attachments is null
            ? []
            : attachments.Where(static attachment => attachment is not null).ToArray();
    }

    public IReadOnlyList<ConversationAttachment> Attachments { get; }

    public string ProfileName => Session.AgentProfileName;

    public IConversationProgressSink ProgressSink { get; }

    public ReplSessionContext Session { get; }

    public string SessionId => Session.SessionId;

    public string UserInput { get; }
}
