using StemCode.Application.Models;

namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceFileWriteExecutionResult(
    WorkspaceFileWriteResult Result,
    WorkspaceFileEditTransaction EditTransaction);
