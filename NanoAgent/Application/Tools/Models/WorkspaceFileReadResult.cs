namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileReadResult(
    string Path,
    string RawContent,
    string DisplayContent,
    int StartLine,
    int EndLine,
    int TotalLines,
    bool Truncated,
    int? NextOffset,
    string Sha256,
    string Encoding);
