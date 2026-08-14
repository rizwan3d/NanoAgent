using StemCode.Application.Models;

namespace StemCode.Application.Backend;

public sealed record BackendCommandResult(
    ReplCommandResult CommandResult,
    BackendSessionInfo SessionInfo);
