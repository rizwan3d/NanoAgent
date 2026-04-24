using FluentAssertions;
using NanoAgent.Infrastructure.Mcp;

namespace NanoAgent.Tests.Infrastructure.Mcp;

public sealed class McpToolNameTests
{
    [Fact]
    public void Create_Should_UseCodexStyleMcpPrefix()
    {
        string result = McpToolName.Create("docs", "search", new HashSet<string>(StringComparer.Ordinal));

        result.Should().Be("mcp__docs__search");
    }

    [Fact]
    public void Create_Should_SanitizeAndShortenLongNames()
    {
        string result = McpToolName.Create(
            "server with spaces and punctuation!",
            "tool/with/a/really/long/path/that/would/exceed/the/function/name/limit",
            new HashSet<string>(StringComparer.Ordinal));

        result.Should().StartWith("mcp__");
        result.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            .Should()
            .BeTrue();
        result.Length.Should().BeLessThanOrEqualTo(64);
    }
}
