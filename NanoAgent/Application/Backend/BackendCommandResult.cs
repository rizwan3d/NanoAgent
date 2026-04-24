using NanoAgent.Application.Models;

namespace NanoAgent.Application.Backend;

public sealed record BackendCommandResult(
    ReplCommandResult CommandResult,
    BackendSessionInfo SessionInfo);
