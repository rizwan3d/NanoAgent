using System.Text;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceInstructionsProvider : IWorkspaceInstructionsProvider
{
    private const int MaxInstructionFileCharacters = 24_000;

    private static readonly string[] InstructionFilePaths =
    [
        "AGENTS.md",
        Path.Combine(".agent", "AGENTS.md")
    ];

    public async Task<string?> LoadAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        string workspaceRoot = Path.GetFullPath(session.WorkspacePath);
        List<WorkspaceInstructionFile> instructionFiles = [];

        foreach (string relativePath in InstructionFilePaths)
        {
            string fullPath = WorkspacePath.Resolve(workspaceRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            string normalizedContent = NormalizeContent(content, out bool wasTruncated);
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                continue;
            }

            instructionFiles.Add(new WorkspaceInstructionFile(
                WorkspacePath.ToRelativePath(workspaceRoot, fullPath),
                normalizedContent,
                wasTruncated));
        }

        if (instructionFiles.Count == 0)
        {
            return null;
        }

        StringBuilder builder = new();
        builder.AppendLine("Workspace instructions:");
        builder.AppendLine("The following AGENTS.md files contain persistent instructions for this workspace. Follow them unless they conflict with higher-priority system or developer instructions.");

        foreach (WorkspaceInstructionFile instructionFile in instructionFiles)
        {
            builder.AppendLine();
            builder.Append("<workspace_instruction path=\"");
            builder.Append(instructionFile.RelativePath);
            builder.AppendLine("\">");
            builder.AppendLine(SecretRedactor.Redact(instructionFile.Content));
            if (instructionFile.WasTruncated)
            {
                builder.AppendLine();
                builder.AppendLine("[Instruction file truncated by NanoAgent.]");
            }

            builder.AppendLine("</workspace_instruction>");
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeContent(
        string content,
        out bool wasTruncated)
    {
        string normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= MaxInstructionFileCharacters)
        {
            wasTruncated = false;
            return normalized;
        }

        wasTruncated = true;
        return normalized[..MaxInstructionFileCharacters].TrimEnd();
    }

    private sealed record WorkspaceInstructionFile(
        string RelativePath,
        string Content,
        bool WasTruncated);
}
