using System.Text.Json.Serialization;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Tools.Serialization;

[JsonSerializable(typeof(ToolErrorPayload))]
[JsonSerializable(typeof(WorkspaceFileReadResult))]
[JsonSerializable(typeof(WorkspaceFileWritePreviewLine))]
[JsonSerializable(typeof(WorkspaceFileWriteResult))]
[JsonSerializable(typeof(WorkspaceDirectoryListResult))]
[JsonSerializable(typeof(WorkspaceDirectoryEntry))]
[JsonSerializable(typeof(WorkspaceTextSearchResult))]
[JsonSerializable(typeof(WorkspaceTextSearchMatch))]
[JsonSerializable(typeof(ShellCommandExecutionResult))]
internal sealed partial class ToolJsonContext : JsonSerializerContext
{
}
