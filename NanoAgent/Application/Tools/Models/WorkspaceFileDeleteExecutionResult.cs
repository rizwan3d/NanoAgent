using NanoAgent.Application.Models;

namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileDeleteExecutionResult(
    WorkspaceFileDeleteResult Result,
    WorkspaceFileEditTransaction EditTransaction);
