using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Application.Services;

internal sealed class SessionAppService : ISessionAppService
{
    private readonly IAgentProfileResolver _profileResolver;
    private readonly IConversationSectionStore _sectionStore;
    private readonly IReplSectionService _sectionService;

    public SessionAppService(
        IAgentProfileResolver profileResolver,
        IConversationSectionStore sectionStore,
        IReplSectionService sectionService)
    {
        _profileResolver = profileResolver;
        _sectionStore = sectionStore;
        _sectionService = sectionService;
    }

    public async Task<ReplSessionContext> CreateAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IAgentProfile profile = _profileResolver.Resolve(request.ProfileName);

        ReplSessionContext session = await _sectionService.CreateNewAsync(
            ApplicationIdentity.ProductName,
            request.ProviderProfile,
            request.ActiveModelId,
            request.AvailableModelIds,
            profile,
            request.ModelContextWindowTokens,
            request.ActiveProviderName,
            cancellationToken);

        ApplyReasoningEffort(session, request.ReasoningEffort);
        return session;
    }

    public void EnsureTitleGenerationStarted(
        ReplSessionContext session,
        string firstUserPrompt)
    {
        _sectionService.EnsureTitleGenerationStarted(session, firstUserPrompt);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ConversationSectionSnapshot> snapshots = await _sectionStore.ListAsync(cancellationToken);

        return snapshots
            .Select(static snapshot => new SessionSummary(
                snapshot.SectionId,
                snapshot.Title,
                snapshot.CreatedAtUtc,
                snapshot.UpdatedAtUtc,
                snapshot.ProviderProfile.ProviderKind.ToDisplayName(),
                snapshot.ActiveModelId,
                snapshot.AgentProfileName))
            .ToArray();
    }

    public async Task<ReplSessionContext> ResumeAsync(
        ResumeSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IAgentProfile? profileOverride = string.IsNullOrWhiteSpace(request.ProfileName)
            ? null
            : _profileResolver.Resolve(request.ProfileName);

        ReplSessionContext session = await _sectionService.ResumeAsync(
            ApplicationIdentity.ProductName,
            request.SessionId,
            profileOverride,
            cancellationToken);

        ApplyReasoningEffort(session, request.ReasoningEffortOverride);
        return session;
    }

    public Task SaveIfDirtyAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        return _sectionService.SaveIfDirtyAsync(session, cancellationToken);
    }

    public Task StopAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        return _sectionService.StopAsync(session, cancellationToken);
    }

    private static void ApplyReasoningEffort(
        ReplSessionContext session,
        string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
        {
            return;
        }

        session.SetReasoningEffort(reasoningEffort);
    }
}
