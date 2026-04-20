using NanoAgent.Application.Models;

namespace NanoAgent.Application.Abstractions;

public interface IConversationSectionStore
{
    Task<ConversationSectionSnapshot?> LoadAsync(
        string sectionId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ConversationSectionSnapshot snapshot,
        CancellationToken cancellationToken);
}
