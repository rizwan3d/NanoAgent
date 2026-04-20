using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ConversationSectionSnapshot))]
[JsonSerializable(typeof(ConversationSectionTurn))]
[JsonSerializable(typeof(AgentProviderProfile))]
internal sealed partial class ConversationSectionStorageJsonContext : JsonSerializerContext
{
}
