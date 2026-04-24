using NanoAgent.Application.Models;

namespace NanoAgent.CLI;

public sealed record BackendCommandResult(
    ReplCommandResult CommandResult,
    BackendSessionInfo SessionInfo);
