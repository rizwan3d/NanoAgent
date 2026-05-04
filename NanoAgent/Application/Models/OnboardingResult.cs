using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Models;

public sealed record OnboardingResult(
    AgentProviderProfile Profile,
    bool WasOnboardedDuringCurrentRun,
    string? ReasoningEffort = null,
    string? ActiveProviderName = null);
