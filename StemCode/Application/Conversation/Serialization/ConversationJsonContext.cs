using StemCode.Application.Models;
using System.Text.Json.Serialization;

namespace StemCode.Application.Conversation.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolFeedbackPayload))]
internal sealed partial class ConversationJsonContext : JsonSerializerContext
{
}
