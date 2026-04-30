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

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }
}
