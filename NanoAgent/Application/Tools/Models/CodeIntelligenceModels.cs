namespace NanoAgent.Application.Tools.Models;

public sealed record CodeIntelligenceRequest(
    string Action,
    string Path,
    int? Line,
    int? Character,
    bool IncludeDeclaration,
    int TimeoutSeconds,
    string? Query = null,
    string? NewName = null,
    string? CallDirection = null,
    bool Refresh = false);

public sealed record CodeIntelligenceResult(
    string Action,
    string Path,
    string LanguageId,
    string ServerName,
    IReadOnlyList<CodeIntelligenceItem> Items,
    string? HoverText,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CodeIntelligenceServerStatus>? Servers = null);

public sealed record CodeIntelligenceItem(
    string Kind,
    string? Name,
    string? Detail,
    string Path,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter,
    string? ContainerName);

public sealed record CodeIntelligenceServerStatus(
    string Language,
    string LanguageId,
    IReadOnlyList<string> FileExtensions,
    IReadOnlyList<CodeIntelligenceServerCandidate> Candidates,
    string? SelectedServerName);

public sealed record CodeIntelligenceServerCandidate(
    string Key,
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    int Priority,
    string DetectionStatus,
    string Source,
    string? ResolvedCommand,
    string? InstallHint,
    string LanguageId,
    string? Message);
