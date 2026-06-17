using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceSystemPromptProvider : IWorkspaceSystemPromptProvider
{
    private const string SystemPromptPath = ".nanoagent/SystemPrompt.md";
    private const string SystemPromptAppendPath = ".nanoagent/SystemPrompt-Append.md";

    public async Task<string?> LoadAsync(
        ReplSessionContext session,
        string? configuredSystemPrompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = Path.GetFullPath(session.WorkspacePath);
        string? overridePrompt = await LoadPromptFileAsync(
            workspaceRoot,
            SystemPromptPath,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(overridePrompt))
        {
            return ConversationOptions.CreateSystemPrompt(overridePrompt);
        }

        string? appendedPrompt = await LoadPromptFileAsync(
            workspaceRoot,
            SystemPromptAppendPath,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(appendedPrompt))
        {
            return configuredSystemPrompt;
        }

        return string.IsNullOrWhiteSpace(configuredSystemPrompt)
            ? ConversationOptions.CreateSystemPrompt(appendedPrompt)
            : string.Join(
                $"{Environment.NewLine}{Environment.NewLine}",
                configuredSystemPrompt.Trim(),
                appendedPrompt);
    }

    private static async Task<string?> LoadPromptFileAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string fullPath = WorkspacePath.Resolve(workspaceRoot, relativePath);
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
            : SecretRedactor.Redact(normalizedContent);
    }
}
