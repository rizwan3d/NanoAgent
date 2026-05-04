namespace NanoAgent.Application.Models;

public sealed record ReplCommandResult(
    bool ExitRequested,
    string? Message,
    ReplFeedbackKind FeedbackKind,
    ReplSessionContext? SessionOverride = null,
    bool ReplaySession = false)
{
    public static ReplCommandResult Continue(string? message = null, ReplFeedbackKind feedbackKind = ReplFeedbackKind.Info)
    {
        return new ReplCommandResult(
            ExitRequested: false,
            message,
            feedbackKind);
    }

    public static ReplCommandResult SwitchSession(
        ReplSessionContext session,
        string? message = null,
        ReplFeedbackKind feedbackKind = ReplFeedbackKind.Info,
        bool replaySession = true)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ReplCommandResult(
            ExitRequested: false,
            message,
            feedbackKind,
            session,
            replaySession);
    }

    public static ReplCommandResult Exit(string? message = null)
    {
        return new ReplCommandResult(
            ExitRequested: true,
            message,
            ReplFeedbackKind.Info);
    }
}
