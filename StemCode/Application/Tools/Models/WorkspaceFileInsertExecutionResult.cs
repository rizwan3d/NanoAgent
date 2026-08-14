using StemCode.Application.Models;

namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceFileInsertExecutionResult(
    WorkspaceFileInsertResult Result,
    WorkspaceFileEditTransaction EditTransaction);
