using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Utilities;
using StemCode.Infrastructure.Configuration;

namespace StemCode.Infrastructure.Tools;

internal sealed class WorkspaceSystemPromptProvider : IWorkspaceSystemPromptProvider
{
    private const string SystemPromptPath = ".stemcode/SystemPrompt.md";
    private const string SystemPromptAppendPath = ".stemcode/SystemPrompt-Append.md";

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
