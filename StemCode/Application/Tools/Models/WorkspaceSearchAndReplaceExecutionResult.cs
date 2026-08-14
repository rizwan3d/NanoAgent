using StemCode.Application.Models;

namespace StemCode.Application.Tools.Models;

public sealed record WorkspaceSearchAndReplaceExecutionResult(
    WorkspaceSearchAndReplaceResult Result,
    WorkspaceFileEditTransaction? EditTransaction);
