using FluentAssertions;
using NanoAgent.Application.Backend;

namespace NanoAgent.Tests.Application.Backend;

public sealed class BackendRuntimeOptionsTests
{
    [Fact]
    public void Should_Default_To_Cli_Surface()
    {
        var options = new BackendRuntimeOptions();

        options.AppSurface.Should().Be("cli");
        options.AutoApproveAllTools.Should().BeFalse();
        options.SessionMcpServers.Should().BeEmpty();
    }

    [Fact]
    public void Should_Accept_Custom_Values()
    {
        var mcpServers = new[] { new BackendMcpServerConfiguration("test") };
        var options = new BackendRuntimeOptions(
            sessionMcpServers: mcpServers,
            autoApproveAllTools: true,
            appSurface: "vscode");

        options.AutoApproveAllTools.Should().BeTrue();
        options.AppSurface.Should().Be("vscode");
        options.SessionMcpServers.Should().BeEquivalentTo(mcpServers);
    }

    [Theory]
    [InlineData("cli", "cli")]
    [InlineData("desktop", "desktop")]
    [InlineData("jetbrains", "jetbrains")]
    [InlineData("visual_studio", "visual_studio")]
    [InlineData("vscode", "vscode")]
    [InlineData("CLI", "cli")]
    [InlineData("VSCode", "vscode")]
    [InlineData(null, "cli")]
    [InlineData("", "cli")]
    [InlineData("  ", "cli")]
    [InlineData("unknown", "cli")]
    public void NormalizeAppSurface_Should_ReturnExpected(string? input, string expected)
    {
        string result = BackendRuntimeOptions.NormalizeAppSurface(input);

        result.Should().Be(expected);
    }
}
