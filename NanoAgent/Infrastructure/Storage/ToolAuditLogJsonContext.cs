using NanoAgent.Application.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolAuditRecord))]
internal sealed partial class ToolAuditLogJsonContext : JsonSerializerContext
{
}
