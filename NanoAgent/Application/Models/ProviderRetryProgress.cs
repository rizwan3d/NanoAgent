namespace NanoAgent.Application.Models;

/// <summary>
/// Describes a single provider request retry attempt so the UI can surface
/// progress such as "Trying 1/10 (host not found)" while the executor backs off.
/// </summary>
public sealed record ProviderRetryProgress(
    int Attempt,
    int MaxAttempts,
    string Reason);
