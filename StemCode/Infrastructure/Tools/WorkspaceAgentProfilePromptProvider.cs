using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Profiles;

namespace StemCode.Infrastructure.Tools;

internal sealed class WorkspaceAgentProfilePromptProvider : IWorkspaceAgentProfilePromptProvider
{
    public Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        IAgentProfile? workspaceProfile = WorkspaceAgentProfileLoader
            .Load(session.WorkspacePath)
            .FirstOrDefault(profile =>
                string.Equals(
                    profile.Name,
                    session.AgentProfile.Name,
                    StringComparison.OrdinalIgnoreCase));

        string? prompt = workspaceProfile?.SystemPrompt;
        return Task.FromResult(
            string.IsNullOrWhiteSpace(prompt)
                ? null
                : prompt.Trim());
    }
}
