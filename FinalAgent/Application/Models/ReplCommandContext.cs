namespace FinalAgent.Application.Models;

public sealed record ReplCommandContext(
    string CommandName,
    string Arguments,
    string RawText,
    ReplSessionContext Session);
