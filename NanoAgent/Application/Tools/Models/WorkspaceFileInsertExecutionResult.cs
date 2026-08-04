using NanoAgent.Application.Models;

namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileInsertExecutionResult(
    WorkspaceFileInsertResult Result,
    WorkspaceFileEditTransaction EditTransaction);
