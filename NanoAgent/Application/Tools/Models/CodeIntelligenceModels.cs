namespace NanoAgent.Application.Tools.Models;

public sealed record CodeIntelligenceRequest(
    string Action,
    string Path,
    int? Line,
    int? Character,
    bool IncludeDeclaration,
    int TimeoutSeconds);

public sealed record CodeIntelligenceResult(
    string Action,
    string Path,
    string LanguageId,
    string ServerName,
    IReadOnlyList<CodeIntelligenceItem> Items,
    string? HoverText,
    IReadOnlyList<string> Warnings);

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
