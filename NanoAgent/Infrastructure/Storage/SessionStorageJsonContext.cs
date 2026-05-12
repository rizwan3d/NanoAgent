using NanoAgent.Application.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionRecord))]
[JsonSerializable(typeof(SessionContext))]
[JsonSerializable(typeof(SessionStateSnapshot))]
[JsonSerializable(typeof(SessionFileContext))]
[JsonSerializable(typeof(SessionEditContext))]
[JsonSerializable(typeof(SessionTerminalCommand))]
internal sealed partial class SessionStorageJsonContext : JsonSerializerContext
{
}
