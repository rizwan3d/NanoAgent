using FluentAssertions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Backend;

public sealed class BackendConversationHistoryFormatterTests
{
    [Fact]
    public void Create_Should_RenderSavedToolCallPreview_When_OlderTurnHasNoToolOutputMessages()
    {
        ReplSessionContext session = CreateSession();
        session.AddConversationTurn(
            "write the readme",
            "I updated it.",
            [
                new ConversationToolCall(
                    "call_1",
                    "file_write",
                    """{ "path": "README.md", "content": "# Hello" }""")
            ]);

        IReadOnlyList<BackendConversationMessage> history = BackendConversationHistoryFormatter.Create(session);

        history.Should().HaveCount(3);
        history[0].Role.Should().Be("user");
        history[1].Role.Should().Be("tool");
        history[1].Content.Should().StartWith("\u2022 Previewed saved tool call: file write: README.md");
        history[1].Content.Should().Contain("result output was not stored in this older section");
        history[1].Content.Should().Contain("content: 7 chars");
        history[2].Role.Should().Be("assistant");
    }

    [Fact]
    public void Create_Should_PreferStoredToolOutputMessages_When_Present()
    {
        ReplSessionContext session = CreateSession();
        session.AddConversationTurn(
            "read the readme",
            "I read it.",
            [
                new ConversationToolCall(
                    "call_1",
                    "file_read",
                    """{ "path": "README.md" }""")
            ],
            ["\u2022 Read README.md (12 chars)"]);

        IReadOnlyList<BackendConversationMessage> history = BackendConversationHistoryFormatter.Create(session);

        history.Should().HaveCount(3);
        history[1].Role.Should().Be("tool");
        history[1].Content.Should().Be("\u2022 Read README.md (12 chars)");
        history[1].Content.Should().NotContain("Previewed saved tool call");
    }

    [Fact]
    public void Create_Should_IncludeAssistantReasoningMetadata_When_Present()
    {
        ReplSessionContext session = CreateSession();
        session.AddConversationTurn(
            "update the README",
            "I updated it.",
            assistantReasoningContent: "The README needs a concise edit.",
            assistantReasoningDetailsJson: """[{ "summary": "Use a short patch." }]""");

        IReadOnlyList<BackendConversationMessage> history = BackendConversationHistoryFormatter.Create(session);

        history.Should().HaveCount(2);
        history[1].Role.Should().Be("assistant");
        history[1].Content.Should().Be("I updated it.");
        history[1].ReasoningContent.Should().Be("The README needs a concise edit.");
        history[1].ReasoningDetailsJson.Should().Contain("Use a short patch.");
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }
}
