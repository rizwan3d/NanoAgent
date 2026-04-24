using System.Text.Json.Serialization;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Permissions;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolPermissionPolicy))]
[JsonSerializable(typeof(FilePathPermissionRule))]
[JsonSerializable(typeof(PatchPermissionPolicy))]
[JsonSerializable(typeof(PermissionRule))]
[JsonSerializable(typeof(PermissionSettings))]
[JsonSerializable(typeof(ShellCommandPermissionPolicy))]
[JsonSerializable(typeof(ToolSandboxMode))]
[JsonSerializable(typeof(WebRequestPermissionPolicy))]
internal sealed partial class PermissionJsonContext : JsonSerializerContext
{
}
