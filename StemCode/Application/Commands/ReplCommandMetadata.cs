namespace StemCode.Application.Commands;

public sealed record ReplCommandMetadata(
    string CommandName,
    string Description,
    string Usage,
    bool RequiresArgument = false);
