using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceSystemPromptProvider : IWorkspaceSystemPromptProvider
{
    private const string SystemPromptPath = ".nanoagent/SystemPrompt.md";

    public async Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = Path.GetFullPath(session.WorkspacePath);
        string fullPath = WorkspacePath.Resolve(workspaceRoot, SystemPromptPath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        string normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        return string.IsNullOrWhiteSpace(normalizedContent)
            ? null
            : ConversationOptions.CreateSystemPrompt(SecretRedactor.Redact(normalizedContent));
    }
}
