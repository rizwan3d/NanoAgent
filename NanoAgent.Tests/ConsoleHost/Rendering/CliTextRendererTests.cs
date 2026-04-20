using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;

namespace NanoAgent.Tests.ConsoleHost.Rendering;

public sealed class CliTextRendererTests
{
    [Fact]
    public async Task RenderAsync_Should_RenderAssistantMarkdownWithSpectreWidgets_When_MessageContainsListsCodeAndQuotes()
    {
        FakeConsoleTerminal terminal = new();
        var console = SpectreConsoleFactory.Create(terminal);
        ConsoleCliOutputTarget outputTarget = new(console);
        CliTextRenderer sut = new(
            outputTarget,
            console,
            new ConsoleRenderSettings
            {
                EnableAnimations = false
            });
        MarkdownLikeCliMessageFormatter formatter = new();

        CliRenderDocument document = formatter.Format(
            CliRenderMessageKind.Assistant,
            """
            ## Review
            Use `dotnet test` before shipping.

            - Add regression coverage
            - Update docs

            > Watch the edge cases.

            ```diff
            + added
            - removed
            ```
            """);

        await sut.RenderAsync(document, CancellationToken.None);

        terminal.Output.Should().Contain("assistant");
        terminal.Output.Should().Contain("Review");
        terminal.Output.Should().Contain("• Add regression coverage");
        terminal.Output.Should().Contain("• Update docs");
        terminal.Output.Should().Contain("│ Watch the edge cases.");
        terminal.Output.Should().Contain("diff");
        terminal.Output.Should().Contain("+ added");
        terminal.Output.Should().Contain("- removed");
    }

    [Fact]
    public async Task RenderAsync_Should_RenderWarningAndErrorPrefixes_When_DocumentIsStatusMessage()
    {
        FakeConsoleTerminal terminal = new();
        var console = SpectreConsoleFactory.Create(terminal);
        ConsoleCliOutputTarget outputTarget = new(console);
        CliTextRenderer sut = new(
            outputTarget,
            console,
            new ConsoleRenderSettings
            {
                EnableAnimations = false
            });
        MarkdownLikeCliMessageFormatter formatter = new();

        await sut.RenderAsync(
            formatter.Format(CliRenderMessageKind.Warning, "Check the generated patch."),
            CancellationToken.None);

        await sut.RenderAsync(
            formatter.Format(CliRenderMessageKind.Error, "The provider request failed."),
            CancellationToken.None);

        terminal.Output.Should().Contain("[warning] Check the generated patch.");
        terminal.Output.Should().Contain("[error] The provider request failed.");
    }

    [Fact]
    public async Task RenderAsync_Should_RenderMarkdownTable_When_MessageContainsPipeTableInsideMarkdownFence()
    {
        FakeConsoleTerminal terminal = new();
        var console = SpectreConsoleFactory.Create(terminal);
        ConsoleCliOutputTarget outputTarget = new(console);
        CliTextRenderer sut = new(
            outputTarget,
            console,
            new ConsoleRenderSettings
            {
                EnableAnimations = false
            });
        MarkdownLikeCliMessageFormatter formatter = new();

        CliRenderDocument document = formatter.Format(
            CliRenderMessageKind.Assistant,
            """
            ```markdown
            | Agent | Focus | Score |
            | :---- | :---: | ----: |
            | NanoAgent | Terminal UX | 92 |
            | Worker | Patches | 87 |
            ```
            """);

        await sut.RenderAsync(document, CancellationToken.None);

        terminal.Output.Should().Contain("assistant");
        terminal.Output.Should().Contain("Agent");
        terminal.Output.Should().Contain("Focus");
        terminal.Output.Should().Contain("Score");
        terminal.Output.Should().Contain("NanoAgent");
        terminal.Output.Should().Contain("Terminal UX");
        terminal.Output.Should().Contain("92");
        terminal.Output.Should().Contain("\u256d");
        terminal.Output.Should().NotContain("markdown");
        terminal.Output.Should().NotContain("| Agent | Focus | Score |");
    }
}
