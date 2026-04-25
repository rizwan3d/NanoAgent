using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Conversation;

[JsonSerializable(typeof(OpenAiChatCompletionRequest))]
[JsonSerializable(typeof(OpenAiChatCompletionResponse))]
[JsonSerializable(typeof(OpenAiResponsesRequest))]
internal sealed partial class OpenAiConversationJsonContext : JsonSerializerContext
{
}
