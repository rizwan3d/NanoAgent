using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using System.Text;

namespace NanoAgent.Application.Commands;

internal sealed class CodebaseIndexCommandHandler : IReplCommandHandler
{
    private readonly ICodebaseIndexService _codebaseIndexService;

    public CodebaseIndexCommandHandler(ICodebaseIndexService codebaseIndexService)
    {
        _codebaseIndexService = codebaseIndexService;
    }

    public string CommandName => "index";

    public string Description => "Update, rebuild, inspect, or list the local codebase index.";

    public string Usage => "/index [update|status|rebuild|list] [limit]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string[] args = SplitArguments(context.ArgumentText);
        string action = args.Length == 0
            ? "update"
            : args[0].ToLowerInvariant();

        try
        {
            return action switch
            {
                "update" or "build" => await UpdateAsync(force: false, cancellationToken),
                "rebuild" or "force" => await UpdateAsync(force: true, cancellationToken),
                "status" => await StatusAsync(cancellationToken),
                "list" => await ListAsync(args, cancellationToken),
                "help" or "-h" or "--help" => ReplCommandResult.Continue(Usage),
                _ => ReplCommandResult.Continue(
                    $"Unknown index action '{args[0]}'. Usage: {Usage}",
                    ReplFeedbackKind.Error)
            };
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            IOException or
            UnauthorizedAccessException)
        {
            return ReplCommandResult.Continue(
                $"Codebase index command failed: {exception.Message}",
                ReplFeedbackKind.Error);
        }
    }

    private async Task<ReplCommandResult> UpdateAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        CodebaseIndexBuildResult result = await _codebaseIndexService.BuildAsync(
            force,
            cancellationToken);

        string mode = force ? "rebuilt" : "updated";

        return ReplCommandResult.Continue(
            $"""
            Codebase index {mode}.
            Path: {result.IndexPath}
            Indexed files: {result.IndexedFileCount}
            Added: {result.AddedFileCount}
            Updated: {result.UpdatedFileCount}
            Removed: {result.RemovedFileCount}
            Reused: {result.ReusedFileCount}
            Skipped: {result.SkippedFileCount}
            Semantic symbols: {result.Stats.SemanticSymbolCount}
            Dependencies: {result.Stats.DependencyEdgeCount}
            Calls: {result.Stats.CallEdgeCount}
            Owned files: {result.Stats.OwnedFileCount}
            Ownership rules: {result.Stats.OwnershipRuleCount}
            Duration: {result.DurationMilliseconds} ms
            {FormatWarnings(result.Warnings)}
            """.Trim(),
            ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> StatusAsync(CancellationToken cancellationToken)
    {
        CodebaseIndexStatusResult result = await _codebaseIndexService.GetStatusAsync(
            cancellationToken);

        StringBuilder message = new();

        message.AppendLine("Codebase index status");
        message.AppendLine($"Path: {result.IndexPath}");
        message.AppendLine($"Exists: {result.Exists}");
        message.AppendLine($"Stale: {result.IsStale}");
        message.AppendLine($"Built: {(result.BuiltAtUtc is null ? "never" : result.BuiltAtUtc.Value.ToString("u"))}");
        message.AppendLine($"Indexed files: {result.IndexedFileCount}");
        message.AppendLine($"Workspace files: {result.WorkspaceFileCount}");
        message.AppendLine($"New: {result.NewFileCount}");
        message.AppendLine($"Changed: {result.ChangedFileCount}");
        message.AppendLine($"Deleted: {result.DeletedFileCount}");
        message.AppendLine($"Skipped: {result.SkippedFileCount}");
        message.AppendLine($"Semantic symbols: {result.Stats.SemanticSymbolCount}");
        message.AppendLine($"Dependencies: {result.Stats.DependencyEdgeCount}");
        message.AppendLine($"Calls: {result.Stats.CallEdgeCount}");
        message.AppendLine($"Owned files: {result.Stats.OwnedFileCount}");
        message.AppendLine($"Ownership rules: {result.Stats.OwnershipRuleCount}");

        AppendSamples(message, "New files", result.SampleNewFiles);
        AppendSamples(message, "Changed files", result.SampleChangedFiles);
        AppendSamples(message, "Deleted files", result.SampleDeletedFiles);
        AppendWarnings(message, result.Warnings);

        return ReplCommandResult.Continue(
            message.ToString().TrimEnd(),
            result.IsStale ? ReplFeedbackKind.Warning : ReplFeedbackKind.Info);
    }

    private async Task<ReplCommandResult> ListAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        int limit = 200;
        if (args.Count >= 2 &&
            (!int.TryParse(args[1], out limit) || limit <= 0))
        {
            return ReplCommandResult.Continue(
                $"Invalid list limit '{args[1]}'. Usage: {Usage}",
                ReplFeedbackKind.Error);
        }

        CodebaseIndexListResult result = await _codebaseIndexService.ListAsync(
            limit,
            cancellationToken);

        if (result.Files.Count == 0)
        {
            return ReplCommandResult.Continue(
                "No files are indexed yet. Run /index update first.",
                ReplFeedbackKind.Info);
        }

        return ReplCommandResult.Continue(
            $"""
            Indexed files: {result.TotalIndexedFileCount}
            Showing: {result.ReturnedFileCount}
            Path: {result.IndexPath}
            Semantic symbols: {result.Stats.SemanticSymbolCount}
            Dependencies: {result.Stats.DependencyEdgeCount}
            Calls: {result.Stats.CallEdgeCount}
            Owned files: {result.Stats.OwnedFileCount}
            Ownership rules: {result.Stats.OwnershipRuleCount}
            {FormatWarnings(result.Warnings)}

            {string.Join(Environment.NewLine, result.Files)}
            """.Trim(),
            ReplFeedbackKind.Info);
    }

    private static string[] SplitArguments(string? argumentText)
    {
        return string.IsNullOrWhiteSpace(argumentText)
            ? []
            : argumentText.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void AppendSamples(
        StringBuilder message,
        string label,
        IReadOnlyList<string> samples)
    {
        if (samples.Count == 0)
        {
            return;
        }

        message.AppendLine($"{label}: {string.Join(", ", samples.Take(5))}");
    }

    private static void AppendWarnings(
        StringBuilder message,
        IReadOnlyList<string> warnings)
    {
        foreach (string warning in warnings)
        {
            message.AppendLine($"Warning: {warning}");
        }
    }

    private static string FormatWarnings(IReadOnlyList<string> warnings)
    {
        return warnings.Count == 0
            ? "Warnings: none"
            : $"Warnings: {string.Join(" | ", warnings)}";
    }
}
