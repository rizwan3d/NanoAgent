using System.Text.Json.Serialization;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Tools.Serialization;

[JsonSerializable(typeof(ToolErrorPayload))]
[JsonSerializable(typeof(AgentDelegationResult))]
[JsonSerializable(typeof(PlanUpdateItem))]
[JsonSerializable(typeof(PlanUpdateResult))]
[JsonSerializable(typeof(PlanningModeResult))]
[JsonSerializable(typeof(WorkspaceApplyPatchFileResult))]
[JsonSerializable(typeof(WorkspaceApplyPatchResult))]
[JsonSerializable(typeof(WorkspaceFileReadResult))]
[JsonSerializable(typeof(WorkspaceFileSearchResult))]
[JsonSerializable(typeof(WorkspaceFileWritePreviewLine))]
[JsonSerializable(typeof(WorkspaceFileWriteResult))]
[JsonSerializable(typeof(WorkspaceDirectoryListResult))]
[JsonSerializable(typeof(WorkspaceDirectoryEntry))]
[JsonSerializable(typeof(WorkspaceTextSearchResult))]
[JsonSerializable(typeof(WorkspaceTextSearchMatch))]
[JsonSerializable(typeof(WebSearchResult))]
[JsonSerializable(typeof(WebSearchResultItem))]
[JsonSerializable(typeof(ShellCommandExecutionResult))]
internal sealed partial class ToolJsonContext : JsonSerializerContext
{
}
