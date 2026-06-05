using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Commands;

internal sealed class LspCommandHandler : IReplCommandHandler
{
    private readonly ICodeIntelligenceService _codeIntelligenceService;

    public LspCommandHandler(ICodeIntelligenceService codeIntelligenceService)
    {
        _codeIntelligenceService = codeIntelligenceService;
    }

    public string CommandName => "lsp";

    public string Description => "Show discovered language servers, or inspect which ones apply to a file.";

    public string Usage => "/lsp [status|refresh|file <path> [refresh]]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        LspCommandRequest request = ParseRequest(context);
        if (request.ErrorMessage is not null)
        {
            return ReplCommandResult.Continue(request.ErrorMessage, ReplFeedbackKind.Error);
        }

        CodeIntelligenceResult result = await _codeIntelligenceService.QueryAsync(
            new CodeIntelligenceRequest(
                "servers_status",
                ".",
                null,
                null,
                false,
                10,
                Refresh: request.Refresh),
            cancellationToken);

        string message = request.FilePath is null
            ? LspCommandSupport.FormatStatus(result)
            : LspCommandSupport.FormatFileStatus(result, context.Session, request.FilePath);
        return ReplCommandResult.Continue(message);
    }

    private static LspCommandRequest ParseRequest(ReplCommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            return new LspCommandRequest(null, Refresh: false, ErrorMessage: null);
        }

        if (context.Arguments.Count == 1)
        {
            string argument = context.Arguments[0];
            if (string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase))
            {
                return new LspCommandRequest(null, Refresh: false, ErrorMessage: null);
            }

            if (string.Equals(argument, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                return new LspCommandRequest(null, Refresh: true, ErrorMessage: null);
            }
        }

        if (string.Equals(context.Arguments[0], "file", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Arguments.Count < 2)
            {
                return new LspCommandRequest(null, false, "Usage: /lsp file <path> [refresh]");
            }

            bool refresh = context.Arguments.Skip(2)
                .Any(static argument => string.Equals(argument, "refresh", StringComparison.OrdinalIgnoreCase));
            return new LspCommandRequest(context.Arguments[1], refresh, null);
        }

        return new LspCommandRequest(null, false, "Usage: /lsp [status|refresh|file <path> [refresh]]");
    }

    private sealed record LspCommandRequest(
        string? FilePath,
        bool Refresh,
        string? ErrorMessage);
}

internal static class LspCommandSupport
{
    public static string FormatStatus(CodeIntelligenceResult result)
    {
        if (result.Servers is null || result.Servers.Count == 0)
        {
            return "No language servers are registered.";
        }

        List<string> lines = ["Language servers:"];
        foreach (CodeIntelligenceServerStatus server in result.Servers.OrderBy(static item => item.Language, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{server.Language} ({string.Join(", ", server.FileExtensions)})");
            if (!string.IsNullOrWhiteSpace(server.SelectedServerName))
            {
                lines.Add($"Selected: {server.SelectedServerName}");
            }

            foreach (CodeIntelligenceServerCandidate candidate in server.Candidates)
            {
                lines.Add(FormatCandidate(candidate));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatFileStatus(
        CodeIntelligenceResult result,
        ReplSessionContext session,
        string requestedPath)
    {
        string resolvedPath;
        try
        {
            resolvedPath = session.ResolvePathFromWorkingDirectory(requestedPath);
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        string extension = Path.GetExtension(resolvedPath);
        CodeIntelligenceServerStatus[] matches = (result.Servers ?? [])
            .Where(server => server.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .OrderBy(static server => server.Language, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            return $"No language servers are registered for '{resolvedPath}' ({extension}).";
        }

        List<string> lines = [$"LSP candidates for {resolvedPath} ({extension}):"];
        foreach (CodeIntelligenceServerStatus match in matches)
        {
            lines.Add($"{match.Language}");
            if (!string.IsNullOrWhiteSpace(match.SelectedServerName))
            {
                lines.Add($"Selected: {match.SelectedServerName}");
            }

            foreach (CodeIntelligenceServerCandidate candidate in match.Candidates)
            {
                lines.Add(FormatCandidate(candidate));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatCandidate(CodeIntelligenceServerCandidate candidate)
    {
        string resolved = string.IsNullOrWhiteSpace(candidate.ResolvedCommand)
            ? candidate.Command
            : $"{candidate.Command} -> {candidate.ResolvedCommand}";
        string message = string.IsNullOrWhiteSpace(candidate.Message)
            ? string.Empty
            : $" ({candidate.Message})";
        string installHint = string.IsNullOrWhiteSpace(candidate.InstallHint)
            ? string.Empty
            : $" Install: {candidate.InstallHint}";
        return $"- [{candidate.DetectionStatus}] {candidate.Name} (priority {candidate.Priority}) {resolved}{message}{installHint}";
    }
}
