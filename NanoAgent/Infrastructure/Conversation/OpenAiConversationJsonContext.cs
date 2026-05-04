using System.Text.Json.Serialization;

namespace NanoAgent.Infrastructure.Conversation;

[JsonSerializable(typeof(OpenAiChatCompletionRequest))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiChatCompletionContentPart>))]
[JsonSerializable(typeof(OpenAiChatCompletionResponse))]
[JsonSerializable(typeof(OpenAiResponsesRequest))]
[JsonSerializable(typeof(AnthropicMessagesRequest))]
[JsonSerializable(typeof(AnthropicMessagesResponse))]
[JsonSerializable(typeof(string))]
internal sealed partial class OpenAiConversationJsonContext : JsonSerializerContext
{
}
