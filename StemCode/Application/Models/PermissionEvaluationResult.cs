namespace StemCode.Application.Models;

public sealed class PermissionEvaluationResult
{
    private PermissionEvaluationResult(
        PermissionEvaluationDecision decision,
        string? reasonCode = null,
        string? reason = null,
        PermissionMode? effectiveMode = null,
        PermissionRequestDescriptor? request = null)
    {
        if (decision != PermissionEvaluationDecision.Allowed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        }

        Decision = decision;
        EffectiveMode = effectiveMode;
        Request = request;
        ReasonCode = reasonCode?.Trim();
        Reason = reason?.Trim();
    }

    public PermissionEvaluationDecision Decision { get; }

    public PermissionMode? EffectiveMode { get; }

    public bool IsAllowed => Decision == PermissionEvaluationDecision.Allowed;

    public string? Reason { get; }

    public string? ReasonCode { get; }

    public PermissionRequestDescriptor? Request { get; }

    public static PermissionEvaluationResult Allowed(
        PermissionMode? effectiveMode = null,
        PermissionRequestDescriptor? request = null)
    {
        return new PermissionEvaluationResult(
            PermissionEvaluationDecision.Allowed,
            effectiveMode: effectiveMode,
            request: request);
    }

    public static PermissionEvaluationResult Denied(
        string reasonCode,
        string reason,
        PermissionMode? effectiveMode = null,
        PermissionRequestDescriptor? request = null)
    {
        return new PermissionEvaluationResult(
            PermissionEvaluationDecision.Denied,
            reasonCode,
            reason,
            effectiveMode,
            request);
    }

    public static PermissionEvaluationResult RequiresApproval(
        string reasonCode,
        string reason,
        PermissionMode? effectiveMode = null,
        PermissionRequestDescriptor? request = null)
    {
        return new PermissionEvaluationResult(
            PermissionEvaluationDecision.RequiresApproval,
            reasonCode,
            reason,
            effectiveMode,
            request);
    }
}
