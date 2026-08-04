using NanoAgent.Application.Models;

namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceSearchAndReplaceExecutionResult(
    WorkspaceSearchAndReplaceResult Result,
    WorkspaceFileEditTransaction? EditTransaction);
