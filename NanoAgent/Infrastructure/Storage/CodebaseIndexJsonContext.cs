using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(CodebaseIndexDocument))]
internal sealed partial class CodebaseIndexJsonContext : JsonSerializerContext
{
}
