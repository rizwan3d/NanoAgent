using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IWorkspaceSystemPromptProvider
{
    Task<string?> LoadAsync(
        ReplSessionContext session,
        string? configuredSystemPrompt,
        CancellationToken cancellationToken);
}
