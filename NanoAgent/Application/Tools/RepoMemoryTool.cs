using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Application.Utilities;
using System.Text;

namespace NanoAgent.Application.Tools;

internal sealed class RepoMemoryTool : ITool
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly MemorySettings _memorySettings;

    public RepoMemoryTool(MemorySettings? memorySettings = null)
    {
        _memorySettings = memorySettings ?? new MemorySettings();
    }

    public string Description => "List, read, or write structured repo-scoped team memory markdown files under .nanoagent/memory. These files are ordinary project files that teams can inspect, diff, and version-control. Writes require memory approval.";

    public string Name => AgentToolNames.RepoMemory;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["memory"]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["list", "read", "write"],
              "description": "Repo memory operation to run."
            },
            "document": {
              "type": "string",
              "enum": ["architecture", "conventions", "decisions", "known-issues", "test-strategy"],
              "description": "Structured memory document to read or write."
            },
            "content": {
              "type": "string",
              "description": "Full markdown content for write. Read the document first and write the complete revised file."
            }
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "action", out string? action))
        {
            return InvalidArguments(
                "missing_action",
                "Tool 'repo_memory' requires an action: list, read, or write.");
        }

        return action!.ToLowerInvariant() switch
        {
            "list" => await ListAsync(context, cancellationToken),
            "read" => await ReadAsync(context, cancellationToken),
            "write" => await WriteAsync(context, cancellationToken),
            _ => InvalidArguments(
                "invalid_action",
                $"Tool 'repo_memory' received unsupported action '{action}'.")
        };
    }

    private static async Task<ToolResult> ListAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<RepoMemoryDocumentResult> documents = [];
        foreach (RepoMemoryDocumentDefinition document in RepoMemoryDocuments.All)
        {
            documents.Add(await CreateDocumentResultAsync(
                context.Session.WorkspacePath,
                document,
                includeContent: false,
                cancellationToken));
        }

        string message = $"Listed {documents.Count} repo memory documents.";
        return Success(
            "list",
            message,
            documents,
            "Repo memory",
            FormatDocumentList(documents));
    }

    private static async Task<ToolResult> ReadAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetDocument(context, out RepoMemoryDocumentDefinition? document, out ToolResult? invalidResult))
        {
            return invalidResult!;
        }

        RepoMemoryDocumentResult result = await CreateDocumentResultAsync(
            context.Session.WorkspacePath,
            document!,
            includeContent: true,
            cancellationToken);

        if (!result.Exists)
        {
            return ToolResultFactory.NotFound(
                "repo_memory_document_not_found",
                $"Repo memory document '{document!.Name}' does not exist at {result.Path}.",
                new ToolRenderPayload(
                    "Repo memory document not found",
                    $"{result.Path} does not exist yet. Use action 'write' to create it after approval."));
        }

        return Success(
            "read",
            $"Read repo memory document '{document!.Name}'.",
            [result],
            $"Repo memory: {result.Title}",
            string.IsNullOrWhiteSpace(result.Content)
                ? $"{result.Path} is empty."
                : result.Content!);
    }

    private async Task<ToolResult> WriteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetDocument(context, out RepoMemoryDocumentDefinition? document, out ToolResult? invalidResult))
        {
            return invalidResult!;
        }

        if (!ToolArguments.TryGetString(context.Arguments, "content", out string? content, trim: false) ||
            string.IsNullOrWhiteSpace(content))
        {
            return InvalidArguments(
                "missing_content",
                "Tool 'repo_memory' write requires non-empty markdown 'content'.");
        }

        string normalizedContent = NormalizeContent(content!);
        if (_memorySettings.RedactSecrets)
        {
            normalizedContent = SecretRedactor.Redact(normalizedContent);
        }

        string workspaceRoot = Path.GetFullPath(context.Session.WorkspacePath);
        string relativePath = RepoMemoryDocuments.GetRelativePath(document!);
        string fullPath = WorkspacePath.Resolve(workspaceRoot, relativePath);

        WorkspaceFileEditState beforeState = await CaptureStateAsync(
            workspaceRoot,
            fullPath,
            cancellationToken);

        string? parentDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await File.WriteAllTextAsync(
            fullPath,
            normalizedContent,
            Utf8NoBom,
            cancellationToken);

        WorkspaceFileEditState afterState = await CaptureStateAsync(
            workspaceRoot,
            fullPath,
            cancellationToken);

        context.Session.RecordFileEditTransaction(new WorkspaceFileEditTransaction(
            $"repo_memory ({relativePath})",
            [beforeState],
            [afterState]));

        RepoMemoryDocumentResult result = await CreateDocumentResultAsync(
            workspaceRoot,
            document!,
            includeContent: true,
            cancellationToken);

        string verb = beforeState.Exists ? "Updated" : "Created";
        return Success(
            "write",
            $"{verb} repo memory document '{document!.Name}'.",
            [result],
            $"Repo memory written: {result.Title}",
            $"{verb} {result.Path} ({result.CharacterCount} characters).");
    }

    private static bool TryGetDocument(
        ToolExecutionContext context,
        out RepoMemoryDocumentDefinition? document,
        out ToolResult? invalidResult)
    {
        document = null;
        invalidResult = null;

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "document", out string? documentName))
        {
            invalidResult = InvalidArguments(
                "missing_document",
                "Tool 'repo_memory' requires a 'document' value for read or write.");
            return false;
        }

        if (!RepoMemoryDocuments.TryResolve(documentName, out document) || document is null)
        {
            invalidResult = InvalidArguments(
                "invalid_document",
                $"Tool 'repo_memory' received unknown document '{documentName}'. Valid documents: {string.Join(", ", RepoMemoryDocuments.All.Select(static item => item.Name))}.");
            return false;
        }

        return true;
    }

    private static async Task<RepoMemoryDocumentResult> CreateDocumentResultAsync(
        string workspaceRoot,
        RepoMemoryDocumentDefinition document,
        bool includeContent,
        CancellationToken cancellationToken)
    {
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string relativePath = RepoMemoryDocuments.GetRelativePath(document);
        string fullPath = WorkspacePath.Resolve(fullWorkspaceRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            return new RepoMemoryDocumentResult(
                document.Name,
                relativePath,
                document.Title,
                document.Description,
                Exists: false,
                IsEmpty: true,
                CharacterCount: 0,
                Content: includeContent ? string.Empty : null);
        }

        string content = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);
        string normalizedContent = NormalizeReadContent(content);
        return new RepoMemoryDocumentResult(
            document.Name,
            relativePath,
            document.Title,
            document.Description,
            Exists: true,
            IsEmpty: string.IsNullOrWhiteSpace(normalizedContent),
            CharacterCount: normalizedContent.Length,
            Content: includeContent ? normalizedContent : null);
    }

    private static async Task<WorkspaceFileEditState> CaptureStateAsync(
        string workspaceRoot,
        string fullPath,
        CancellationToken cancellationToken)
    {
        string relativePath = WorkspacePath.ToRelativePath(workspaceRoot, fullPath);
        if (!File.Exists(fullPath))
        {
            return new WorkspaceFileEditState(
                relativePath,
                exists: false,
                content: null);
        }

        string content = await File.ReadAllTextAsync(
            fullPath,
            Encoding.UTF8,
            cancellationToken);
        return new WorkspaceFileEditState(
            relativePath,
            exists: true,
            content);
    }

    private static string NormalizeContent(string content)
    {
        return NormalizeReadContent(content).Trim() + Environment.NewLine;
    }

    private static string NormalizeReadContent(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static string FormatDocumentList(IReadOnlyList<RepoMemoryDocumentResult> documents)
    {
        return string.Join(
            Environment.NewLine,
            documents.Select(static document =>
            {
                string state = document.Exists
                    ? document.IsEmpty
                        ? "empty"
                        : $"{document.CharacterCount} chars"
                    : "missing";
                return $"- {document.Name}: {document.Path} ({state}) - {document.Description}";
            }));
    }

    private static ToolResult Success(
        string action,
        string message,
        IReadOnlyList<RepoMemoryDocumentResult> documents,
        string renderTitle,
        string renderText)
    {
        RepoMemoryToolResult result = new(
            action,
            message,
            documents,
            documents.Count);

        return ToolResultFactory.Success(
            message,
            result,
            ToolJsonContext.Default.RepoMemoryToolResult,
            new ToolRenderPayload(
                renderTitle,
                renderText));
    }

    private static ToolResult InvalidArguments(
        string code,
        string message)
    {
        return ToolResultFactory.InvalidArguments(
            code,
            message,
            new ToolRenderPayload(
                "Invalid repo_memory arguments",
                message));
    }
}
