using System.Text.Json;
using System.Text.Json.Serialization;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Tools.Serialization;

[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ToolErrorPayload))]
[JsonSerializable(typeof(AgentDelegationResult))]
[JsonSerializable(typeof(AgentOrchestrationResult))]
[JsonSerializable(typeof(AgentOrchestrationTaskResult))]
[JsonSerializable(typeof(CodeIntelligenceItem))]
[JsonSerializable(typeof(CodeIntelligenceResult))]
[JsonSerializable(typeof(HeadlessBrowserResult))]
[JsonSerializable(typeof(NanoAgent.Application.Models.LessonMemoryEntry))]
[JsonSerializable(typeof(LessonMemoryToolResult))]
[JsonSerializable(typeof(PlanUpdateItem))]
[JsonSerializable(typeof(PlanUpdateResult))]
[JsonSerializable(typeof(PlanningModeResult))]
[JsonSerializable(typeof(WorkspaceApplyPatchFileResult))]
[JsonSerializable(typeof(WorkspaceApplyPatchResult))]
[JsonSerializable(typeof(WorkspaceFileDeleteResult))]
[JsonSerializable(typeof(WorkspaceFileReadResult))]
[JsonSerializable(typeof(WorkspaceFileSearchResult))]
[JsonSerializable(typeof(WorkspaceFileWritePreviewLine))]
[JsonSerializable(typeof(WorkspaceFileWriteResult))]
[JsonSerializable(typeof(WorkspaceSkillLoadResult))]
[JsonSerializable(typeof(WorkspaceDirectoryListResult))]
[JsonSerializable(typeof(WorkspaceDirectoryEntry))]
[JsonSerializable(typeof(WorkspaceTextSearchResult))]
[JsonSerializable(typeof(WorkspaceTextSearchMatch))]
[JsonSerializable(typeof(WebRunResult))]
[JsonSerializable(typeof(ShellCommandExecutionResult))]
internal sealed partial class ToolJsonContext : JsonSerializerContext
{
}
