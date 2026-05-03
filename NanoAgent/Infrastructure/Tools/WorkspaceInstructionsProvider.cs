using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Text;

namespace NanoAgent.Infrastructure.Tools;

internal sealed class WorkspaceInstructionsProvider : IWorkspaceInstructionsProvider
{
    private const int MaxInstructionFileCharacters = 24_000;
    private const int MaxRepoMemoryFileCharacters = 12_000;

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
        List<RepoMemoryFile> repoMemoryFiles = [];

        foreach (string relativePath in InstructionFilePaths)
        {
            string fullPath = WorkspacePath.Resolve(workspaceRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            string normalizedContent = NormalizeContent(
                content,
                MaxInstructionFileCharacters,
                out bool wasTruncated);
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                continue;
            }

            instructionFiles.Add(new WorkspaceInstructionFile(
                WorkspacePath.ToRelativePath(workspaceRoot, fullPath),
                normalizedContent,
                wasTruncated));
        }

        foreach (RepoMemoryDocumentDefinition document in RepoMemoryDocuments.All)
        {
            string relativePath = RepoMemoryDocuments.GetRelativePath(document);
            string fullPath = WorkspacePath.Resolve(workspaceRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            string normalizedContent = NormalizeContent(
                content,
                MaxRepoMemoryFileCharacters,
                out bool wasTruncated);
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                continue;
            }

            if (RepoMemoryDocuments.IsTemplateContent(document, normalizedContent))
            {
                continue;
            }

            repoMemoryFiles.Add(new RepoMemoryFile(
                WorkspacePath.ToRelativePath(workspaceRoot, fullPath),
                document.Name,
                document.Title,
                normalizedContent,
                wasTruncated));
        }

        if (instructionFiles.Count == 0 &&
            repoMemoryFiles.Count == 0)
        {
            return null;
        }

        StringBuilder builder = new();
        if (instructionFiles.Count > 0)
        {
            AppendWorkspaceInstructions(builder, instructionFiles);
        }

        if (repoMemoryFiles.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            AppendRepoMemory(builder, repoMemoryFiles);
        }

        return builder.ToString().Trim();
    }

    private static void AppendWorkspaceInstructions(
        StringBuilder builder,
        IReadOnlyList<WorkspaceInstructionFile> instructionFiles)
    {
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
    }

    private static void AppendRepoMemory(
        StringBuilder builder,
        IReadOnlyList<RepoMemoryFile> repoMemoryFiles)
    {
        builder.AppendLine("Repo memory:");
        builder.AppendLine("The following .nanoagent/memory/*.md files are repo-scoped team memory: ordinary markdown that the team can inspect, diff, and version-control. Treat them as durable context, verify against current files and tool output, and update them only through approved memory writes when durable team knowledge changes.");

        foreach (RepoMemoryFile memoryFile in repoMemoryFiles)
        {
            builder.AppendLine();
            builder.Append("<repo_memory path=\"");
            builder.Append(memoryFile.RelativePath);
            builder.Append("\" name=\"");
            builder.Append(memoryFile.Name);
            builder.Append("\" title=\"");
            builder.Append(memoryFile.Title);
            builder.AppendLine("\">");
            builder.AppendLine(SecretRedactor.Redact(memoryFile.Content));
            if (memoryFile.WasTruncated)
            {
                builder.AppendLine();
                builder.AppendLine("[Repo memory file truncated by NanoAgent.]");
            }

            builder.AppendLine("</repo_memory>");
        }
    }

    private static string NormalizeContent(
        string content,
        int maxCharacters,
        out bool wasTruncated)
    {
        string normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= maxCharacters)
        {
            wasTruncated = false;
            return normalized;
        }

        wasTruncated = true;
        return normalized[..maxCharacters].TrimEnd();
    }

    private sealed record WorkspaceInstructionFile(
        string RelativePath,
        string Content,
        bool WasTruncated);

    private sealed record RepoMemoryFile(
        string RelativePath,
        string Name,
        string Title,
        string Content,
        bool WasTruncated);
}
