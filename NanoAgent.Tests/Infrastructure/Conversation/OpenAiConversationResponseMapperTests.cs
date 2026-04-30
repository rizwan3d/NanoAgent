using FluentAssertions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Conversation;

namespace NanoAgent.Tests.Infrastructure.Conversation;

public sealed class OpenAiConversationResponseMapperTests
{
    [Fact]
    public void Map_Should_ReturnAssistantMessage_When_ResponseContainsContent()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAi,
            """
            {
              "id": "resp_1",
              "choices": [
                {
                  "message": {
                    "content": "Hello from the provider."
                  }
                }
              ],
              "usage": {
                "prompt_tokens": 11,
                "completion_tokens": 7,
                "total_tokens": 18
              }
            }
            """,
            null));

        response.AssistantMessage.Should().Be("Hello from the provider.");
        response.ToolCalls.Should().BeEmpty();
        response.ResponseId.Should().Be("resp_1");
        response.PromptTokens.Should().Be(11);
        response.CompletionTokens.Should().Be(7);
        response.TotalTokens.Should().Be(18);
    }

    [Fact]
    public void Map_Should_ReturnToolCalls_When_ResponseContainsFunctionCalls()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAiCompatible,
            """
            {
              "choices": [
                {
                  "message": {
                    "tool_calls": [
                      {
                        "id": "call_1",
                        "type": "function",
                        "function": {
                          "name": "use_model",
                          "arguments": "{ \"model\": \"gpt-5-mini\" }"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """,
            "fallback_id"));

        response.AssistantMessage.Should().BeNull();
        response.ToolCalls.Should().ContainSingle();
        response.ToolCalls[0].Name.Should().Be("use_model");
        response.ResponseId.Should().Be("fallback_id");
    }

    [Fact]
    public void Map_Should_ReturnToolCalls_When_ResponseContainsLegacyFunctionCall()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAiCompatible,
            """
            {
              "id": "resp_legacy",
              "choices": [
                {
                  "message": {
                    "content": null,
                    "function_call": {
                      "name": "file_read",
                      "arguments": "{ \"path\": \"README.md\" }"
                    }
                  }
                }
              ]
            }
            """,
            null));

        response.AssistantMessage.Should().BeNull();
        response.ToolCalls.Should().ContainSingle();
        response.ToolCalls[0].Id.Should().Be("legacy_function_call");
        response.ToolCalls[0].Name.Should().Be("file_read");
        response.ToolCalls[0].ArgumentsJson.Should().Be("{ \"path\": \"README.md\" }");
        response.ResponseId.Should().Be("resp_legacy");
    }

    [Fact]
    public void Map_Should_ReturnAssistantMessage_When_ResponseContainsRefusalWithoutContent()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAi,
            """
            {
              "id": "resp_refusal",
              "choices": [
                {
                  "message": {
                    "content": null,
                    "refusal": "I can't help with that request."
                  }
                }
              ]
            }
            """,
            null));

        response.AssistantMessage.Should().Be("I can't help with that request.");
        response.ToolCalls.Should().BeEmpty();
        response.ResponseId.Should().Be("resp_refusal");
    }

    [Fact]
    public void Map_Should_ReturnAssistantMessage_When_ResponseContainsStructuredContentParts()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAiCompatible,
            """
            {
              "choices": [
                {
                  "message": {
                    "content": [
                      {
                        "type": "text",
                        "text": "First paragraph."
                      },
                      {
                        "type": "output_text",
                        "text": {
                          "value": "Second paragraph."
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """,
            "resp_structured"));

        response.AssistantMessage.Should().Be(
            $"First paragraph.{Environment.NewLine}{Environment.NewLine}Second paragraph.");
        response.ToolCalls.Should().BeEmpty();
        response.ResponseId.Should().Be("resp_structured");
    }

    [Fact]
    public void Map_Should_ReturnAssistantMessageAndToolCalls_When_ResponsesPayloadContainsOutputItems()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAiChatGptAccount,
            """
            {
              "id": "resp_account",
              "output": [
                {
                  "type": "message",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "I will inspect the file."
                    }
                  ]
                },
                {
                  "type": "function_call",
                  "call_id": "call_1",
                  "name": "file_read",
                  "arguments": "{ \"path\": \"README.md\" }"
                }
              ],
              "usage": {
                "input_tokens": 22,
                "output_tokens": 9,
                "total_tokens": 31
              }
            }
            """,
            null));

        response.AssistantMessage.Should().Be("I will inspect the file.");
        response.ToolCalls.Should().ContainSingle();
        response.ToolCalls[0].Id.Should().Be("call_1");
        response.ToolCalls[0].Name.Should().Be("file_read");
        response.ToolCalls[0].ArgumentsJson.Should().Be("{ \"path\": \"README.md\" }");
        response.ResponseId.Should().Be("resp_account");
        response.PromptTokens.Should().Be(22);
        response.CompletionTokens.Should().Be(9);
        response.TotalTokens.Should().Be(31);
    }

    [Fact]
    public void Map_Should_IgnoreNullError_When_ResponsesPayloadContainsOutput()
    {
        OpenAiConversationResponseMapper sut = new();

        ConversationResponse response = sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAiChatGptAccount,
            """
            {
              "id": "resp_account",
              "error": null,
              "output": [
                {
                  "type": "message",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "Completed successfully."
                    }
                  ]
                }
              ]
            }
            """,
            null));

        response.AssistantMessage.Should().Be("Completed successfully.");
        response.ResponseId.Should().Be("resp_account");
    }

    [Fact]
    public void Map_Should_ThrowConversationResponseException_When_ResponseHasNoMessageAndNoToolCalls()
    {
        OpenAiConversationResponseMapper sut = new();

        Action action = () => sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAi,
            """
            {
              "choices": [
                {
                  "message": {
                    "content": "   "
                  }
                }
              ]
            }
            """,
            null));

        action.Should().Throw<ConversationResponseException>()
            .WithMessage("*neither assistant content, a refusal, nor usable tool calls*")
            .Which.IsRetryableEmptyResponse.Should().BeFalse();
    }

    [Fact]
    public void Map_Should_MarkEmptyStopResponseAsRetryable_When_ResponseHasNoUsableOutput()
    {
        OpenAiConversationResponseMapper sut = new();

        Action action = () => sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAi,
            """
            {
              "id": "resp_empty",
              "choices": [
                {
                  "finish_reason": "stop",
                  "message": {
                    "content": null
                  }
                }
              ]
            }
            """,
            null));

        ConversationResponseException exception = action.Should()
            .Throw<ConversationResponseException>()
            .WithMessage("*Finish reason: stop*Response id: resp_empty*")
            .Which;
        exception.IsRetryableEmptyResponse.Should().BeTrue();
    }

    [Fact]
    public void Map_Should_MarkRawToolCallMarkupAsRetryable_When_ContentContainsProtocolMarkers()
    {
        OpenAiConversationResponseMapper sut = new();

        Action action = () => sut.Map(new ConversationProviderPayload(
            ProviderKind.OpenAiCompatible,
            """
            {
              "id": "resp_raw_tool",
              "choices": [
                {
                  "finish_reason": "stop",
                  "message": {
                    "content": "<|channel>call:update_plan{plan:[]}<tool_call|>"
                  }
                }
              ]
            }
            """,
            null));

        ConversationResponseException exception = action.Should()
            .Throw<ConversationResponseException>()
            .WithMessage("*raw tool-call markup*Response id: resp_raw_tool*")
            .Which;
        exception.IsRetryableRawToolCallResponse.Should().BeTrue();
        exception.IsRetryableProviderOutput.Should().BeTrue();
    }
}
