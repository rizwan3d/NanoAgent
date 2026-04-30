using NanoAgent.Application.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Application.Permissions;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolPermissionPolicy))]
[JsonSerializable(typeof(FilePathPermissionRule))]
[JsonSerializable(typeof(PatchPermissionPolicy))]
[JsonSerializable(typeof(PermissionRule))]
[JsonSerializable(typeof(PermissionSettings))]
[JsonSerializable(typeof(ShellPermissionSettings))]
[JsonSerializable(typeof(ShellCommandPermissionSettings))]
[JsonSerializable(typeof(ShellCommandPermissionPolicy))]
[JsonSerializable(typeof(ToolSandboxMode))]
[JsonSerializable(typeof(WebRequestPermissionPolicy))]
internal sealed partial class PermissionJsonContext : JsonSerializerContext
{
}
