using StemCode.Application.Models;

namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceApplyPatchExecutionResult(
    WorkspaceApplyPatchResult Result,
    WorkspaceFileEditTransaction? EditTransaction);
