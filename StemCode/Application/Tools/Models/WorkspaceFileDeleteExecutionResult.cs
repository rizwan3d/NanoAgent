using StemCode.Application.Models;

namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceFileDeleteExecutionResult(
    WorkspaceFileDeleteResult Result,
    WorkspaceFileEditTransaction EditTransaction);
