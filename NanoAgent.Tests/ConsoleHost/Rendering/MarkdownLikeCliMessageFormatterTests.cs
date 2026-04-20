using NanoAgent.ConsoleHost.Rendering;
using FluentAssertions;

namespace NanoAgent.Tests.ConsoleHost.Rendering;

public sealed class MarkdownLikeCliMessageFormatterTests
{
    [Fact]
    public void Format_Should_CreateStructuredBlocks_When_AssistantMessageContainsMarkdownLikeContent()
    {
        MarkdownLikeCliMessageFormatter sut = new();

        CliRenderDocument document = sut.Format(
            CliRenderMessageKind.Assistant,
            """
            # Plan
            Run `dotnet test` before **shipping**.

            - Add [docs](https://example.com/docs)
            - Update the changelog

            > Keep the release notes concise.

            ---

            ```csharp
            Console.WriteLine("hello");
            ```

            ```diff
            + added line
            - removed line
            ```
            """);

        document.Kind.Should().Be(CliRenderMessageKind.Assistant);
        document.Blocks.Select(block => block.Kind).Should().Equal(
            CliRenderBlockKind.Heading,
            CliRenderBlockKind.Paragraph,
            CliRenderBlockKind.List,
            CliRenderBlockKind.Quote,
            CliRenderBlockKind.Rule,
            CliRenderBlockKind.CodeBlock,
            CliRenderBlockKind.Diff);

        document.Blocks[1].Lines[0].Segments.Should().Contain(segment =>
            segment.Style == CliInlineStyle.Code &&
            segment.Text == "dotnet test");

        document.Blocks[1].Lines[0].Segments.Should().Contain(segment =>
            segment.Style == CliInlineStyle.Strong &&
            segment.Text == "shipping");

        document.Blocks[2].IsOrderedList.Should().BeFalse();
        document.Blocks[2].Lines[0].Segments.Should().Contain(segment =>
            segment.Style == CliInlineStyle.Link &&
            segment.Text == "docs" &&
            segment.Target == "https://example.com/docs");

        document.Blocks[5].Language.Should().Be("csharp");
        document.Blocks[6].Lines.Select(line => line.Kind).Should().Contain([
            CliRenderLineKind.DiffAddition,
            CliRenderLineKind.DiffRemoval
        ]);
    }

    [Fact]
    public void Format_Should_CreateAlertBlock_When_MessageIsWarning()
    {
        MarkdownLikeCliMessageFormatter sut = new();

        CliRenderDocument document = sut.Format(
            CliRenderMessageKind.Warning,
            "Warning: review the generated diff.");

        document.Blocks.Should().ContainSingle();
        document.Blocks[0].Kind.Should().Be(CliRenderBlockKind.Alert);
        document.Blocks[0].Lines[0].Segments[0].Text.Should().Contain("Warning:");
    }

    [Fact]
    public void Format_Should_ParsePipeTableInsideMarkdownFence_When_MessageContainsMarkdownTable()
    {
        MarkdownLikeCliMessageFormatter sut = new();

        CliRenderDocument document = sut.Format(
            CliRenderMessageKind.Assistant,
            """
            ```markdown
            | Agent | Focus | Score |
            | :---- | :---: | ----: |
            | NanoAgent | Terminal UX | 92 |
            | Worker | Patches | 87 |
            ```
            """);

        document.Blocks.Should().ContainSingle();
        document.Blocks[0].Kind.Should().Be(CliRenderBlockKind.Table);
        document.Blocks[0].HasHeaderRow.Should().BeTrue();
        document.Blocks[0].TableColumnAlignments.Should().Equal(
            CliTableColumnAlignment.Left,
            CliTableColumnAlignment.Center,
            CliTableColumnAlignment.Right);
        document.Blocks[0].Lines.Should().HaveCount(3);
        document.Blocks[0].Lines[0].Cells.Should().NotBeNull();
        document.Blocks[0].Lines[0].Cells![0][0].Text.Should().Be("Agent");
        document.Blocks[0].Lines[1].Cells![1][0].Text.Should().Be("Terminal UX");
    }
}
