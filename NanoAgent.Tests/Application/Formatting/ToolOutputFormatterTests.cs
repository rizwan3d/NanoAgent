using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Formatting;

public sealed class ToolOutputFormatterTests
{
    [Fact]
    public void FormatCallPreview_Should_ShowSavedFileWriteArguments()
    {
        ToolOutputFormatter sut = new();
        ConversationToolCall toolCall = new(
            "call_1",
            "file_write",
            """
            {
              "path": "README.md",
              "content": "# NanoAgent\n\nReady to build.",
              "overwrite": true
            }
            """);

        string preview = sut.FormatCallPreview(toolCall);

        preview.Should().StartWith("\u2022 Previewed saved tool call: file write: README.md");
        preview.Should().Contain("result output was not stored in this older section");
        preview.Should().Contain("path: README.md");
        preview.Should().Contain("overwrite: true");
        preview.Should().Contain("content: 28 chars");
        preview.Should().Contain("1 # NanoAgent");
        preview.Should().Contain("3 Ready to build.");
    }

    [Fact]
    public void FormatCallPreview_Should_RedactSecretsFromSavedArguments()
    {
        ToolOutputFormatter sut = new();
        ConversationToolCall toolCall = new(
            "call_1",
            "shell_command",
            """{ "command": "echo sk-abcdefghijklmnopqrstuvwxyz123456" }""");

        string preview = sut.FormatCallPreview(toolCall);

        preview.Should().Contain("<redacted>");
        preview.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz");
    }
}
