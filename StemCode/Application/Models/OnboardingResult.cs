using StemCode.Domain.Models;

namespace StemCode.Application.Models;

public sealed record OnboardingResult(
    AgentProviderProfile Profile,
    bool WasOnboardedDuringCurrentRun,
    string? ReasoningEffort = null,
    string? ActiveProviderName = null,
    string? ThinkingMode = null);
