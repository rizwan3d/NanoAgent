using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Abstractions;

public interface ISessionAppService
{
    Task<ReplSessionContext> CreateNewAsync(
        string applicationName,
        AgentProviderProfile providerProfile,
        string activeModelId,
        IReadOnlyList<string> availableModelIds,
        string? profileName,
        CancellationToken cancellationToken);

    void EnsureTitleGenerationStarted(
        ReplSessionContext session,
        string firstUserPrompt);

    Task<IReadOnlyList<ConversationSectionSnapshot>> ListAsync(
        CancellationToken cancellationToken);

    Task<ReplSessionContext> ResumeAsync(
        string applicationName,
        string sectionId,
        string? profileName,
        CancellationToken cancellationToken);

    Task SaveIfDirtyAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);

    Task StopAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken);
}
