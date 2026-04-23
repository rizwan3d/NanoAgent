using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Services;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Tools;

public sealed class ShellCommandToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_CommandIsMissing()
    {
        ShellCommandTool sut = new(Mock.Of<IShellCommandService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'command'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredShellResult_When_CommandRuns()
    {
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ExecuteAsync(
                It.Is<ShellCommandExecutionRequest>(request =>
                    request.Command == "dotnet --version" &&
                    request.WorkingDirectory == "NanoAgent"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellCommandExecutionResult(
                "dotnet --version",
                "NanoAgent",
                0,
                "10.0.103",
                string.Empty));

        ShellCommandTool sut = new(shellCommandService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "command": "dotnet --version", "workingDirectory": "NanoAgent" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"ExitCode\":0");
        result.RenderPayload!.Text.Should().Contain("10.0.103");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RecordTerminalHistory_When_CommandRuns()
    {
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<ShellCommandExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellCommandExecutionResult(
                "dotnet test",
                ".",
                0,
                "Passed!",
                string.Empty));

        ShellCommandTool sut = new(shellCommandService.Object);
        ReplSessionContext session = TestSessionFactory.Create();
        using JsonDocument document = JsonDocument.Parse("""{ "command": "dotnet test" }""");
        ToolExecutionContext context = new(
            "call_1",
            "shell_command",
            document.RootElement.Clone(),
            session);

        ToolResult result = await sut.ExecuteAsync(
            context,
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        session.SessionState.TerminalHistory.Should().ContainSingle();
        session.SessionState.TerminalHistory[0].Command.Should().Be("dotnet test");
        session.SessionState.TerminalHistory[0].StandardOutput.Should().Be("Passed!");
    }

    [Fact]
    public void PermissionRequirements_Should_AllowCommonProjectToolchainCommands()
    {
        ToolRegistry registry = new(
            [new ShellCommandTool(Mock.Of<IShellCommandService>())],
            new ToolPermissionParser());

        bool found = registry.TryResolve(
            AgentToolNames.ShellCommand,
            out ToolRegistration? registration);

        found.Should().BeTrue();
        registration.Should().NotBeNull();
        registration!.PermissionPolicy.Shell.Should().NotBeNull();
        registration.PermissionPolicy.Shell!.AllowedCommands.Should().Contain(
        [
            "cd",
            "dotnet",
            "mkdir",
            "npm",
            "npx",
            "node",
            "python",
            "pytest",
            "cargo",
            "go",
            "mvn",
            "gradle",
            "make"
        ]);
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "shell_command",
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
