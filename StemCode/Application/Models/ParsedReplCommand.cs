namespace StemCode.Application.Models;

public sealed record ParsedReplCommand(
    string RawText,
    string CommandName,
    string ArgumentText,
    IReadOnlyList<string> Arguments);
