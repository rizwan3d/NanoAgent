using System.Text.Json.Serialization;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Conversation.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ToolFeedbackPayload))]
[JsonSerializable(typeof(ToolRenderPayload))]
internal sealed partial class ConversationJsonContext : JsonSerializerContext
{
}
