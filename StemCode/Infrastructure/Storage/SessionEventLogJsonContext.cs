using StemCode.Application.Models;
using System.Text.Json.Serialization;

namespace StemCode.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SessionEventRecord))]
internal sealed partial class SessionEventLogJsonContext : JsonSerializerContext
{
}
