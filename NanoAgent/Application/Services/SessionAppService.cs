using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

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

    public async Task<ReplSessionContext> CreateNewAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        string? profileName,
        CancellationToken cancellationToken)
    {
        IAgentProfile profile = _profileResolver.Resolve(profileName);

        return await _sectionService.CreateNewAsync(
            applicationName,
            providerProfile,
            activeModelId,
            availableModelIds,
            profile,
            cancellationToken);
    }

    public void EnsureTitleGenerationStarted(
        ReplSessionContext session,
        string firstUserPrompt)
    {
        _sectionService.EnsureTitleGenerationStarted(session, firstUserPrompt);
    }

    public async Task<IReadOnlyList<ConversationSectionSnapshot>> ListAsync(
        CancellationToken cancellationToken)
    {
        return await _sectionStore.ListAsync(cancellationToken);
    }

    public async Task<ReplSessionContext> ResumeAsync(
        string applicationName,
        string sectionId,
        string? profileName,
        CancellationToken cancellationToken)
    {
        IAgentProfile? profileOverride = string.IsNullOrWhiteSpace(profileName)
            ? null
            : _profileResolver.Resolve(profileName);

        return await _sectionService.ResumeAsync(
            applicationName,
            sectionId,
            profileOverride,
            cancellationToken);
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
}
