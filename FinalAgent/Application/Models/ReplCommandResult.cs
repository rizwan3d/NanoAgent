namespace FinalAgent.Application.Models;

public sealed record ReplCommandResult(
    bool ExitRequested,
    string? Message,
    ReplFeedbackKind FeedbackKind)
{
    public static ReplCommandResult Continue(string? message = null, ReplFeedbackKind feedbackKind = ReplFeedbackKind.Info)
    {
        return new ReplCommandResult(
            ExitRequested: false,
            message,
            feedbackKind);
    }

    public static ReplCommandResult Exit(string? message = null)
    {
        return new ReplCommandResult(
            ExitRequested: true,
            message,
            ReplFeedbackKind.Info);
    }
}
