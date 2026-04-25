namespace NanoAgent.Application.Models;

public sealed class LifecycleHookRunResult
{
    private LifecycleHookRunResult(
        bool isAllowed,
        string? failedHookName = null,
        string? message = null)
    {
        IsAllowed = isAllowed;
        FailedHookName = failedHookName;
        Message = message;
    }

    public string? FailedHookName { get; }

    public bool IsAllowed { get; }

    public string? Message { get; }

    public static LifecycleHookRunResult Allowed()
    {
        return new LifecycleHookRunResult(true);
    }

    public static LifecycleHookRunResult Blocked(
        string failedHookName,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedHookName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new LifecycleHookRunResult(
            false,
            failedHookName.Trim(),
            message.Trim());
    }
}
