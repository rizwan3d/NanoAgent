using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class SelectionModalStateTests
{
    [Fact]
    public void BuildBodyMarkup_Should_TruncateLongDescriptionLines()
    {
        string description = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 20).Select(index => $"line {index}"));

        SelectionModalState<string> modal = SelectionModalState<string>.Create(
            new SelectionPromptRequest<string>(
                "Approve shell command?",
                [
                    new SelectionPromptOption<string>("Allow once", "allow-once"),
                    new SelectionPromptOption<string>("Deny once", "deny-once")
                ],
                Description: description),
            completionToken: new object(),
            onSelected: _ => { });

        string markup = modal.BuildBodyMarkup();

        markup.Should().Contain("line 1");
        markup.Should().Contain("...");
        markup.Should().NotContain("line 20");
        markup.Should().Contain("1. Allow once");
    }
}
