using NanoAgent.Application.Models;
using System.Text.Json.Serialization;

namespace NanoAgent.Application.Conversation.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolFeedbackPayload))]
internal sealed partial class ConversationJsonContext : JsonSerializerContext
{
}
