using System.Text.Json.Serialization;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolAuditRecord))]
internal sealed partial class ToolAuditLogJsonContext : JsonSerializerContext
{
}
