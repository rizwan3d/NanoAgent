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
    public async Task ExecuteAsync_Should_UpdateSessionWorkingDirectory_ForFollowUpFileTools()
    {
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-ShellCwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "ToDoApp"));

        try
        {
            ReplSessionContext session = TestSessionFactory.Create(workspaceRoot);
            Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
            shellCommandService
                .Setup(service => service.ExecuteAsync(
                    It.Is<ShellCommandExecutionRequest>(request =>
                        request.Command == "cd ToDoApp" &&
                        request.WorkingDirectory == "."),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ShellCommandExecutionResult(
                    "cd ToDoApp",
                    ".",
                    0,
                    string.Empty,
                    string.Empty));

            ShellCommandTool shellTool = new(shellCommandService.Object);
            ToolResult shellResult = await shellTool.ExecuteAsync(
                CreateContext("""{ "command": "cd ToDoApp" }""", session),
                CancellationToken.None);

            shellResult.Status.Should().Be(ToolResultStatus.Success);
            shellResult.Message.Should().Contain("Session working directory is now 'ToDoApp'");
            session.WorkingDirectory.Should().Be("ToDoApp");

            Mock<IWorkspaceFileService> workspaceFileService = new(MockBehavior.Strict);
            workspaceFileService
                .Setup(service => service.WriteFileWithTrackingAsync(
                    "ToDoApp/Program.cs",
                    "class Program {}",
                    true,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkspaceFileWriteExecutionResult(
                    new WorkspaceFileWriteResult(
                        "ToDoApp/Program.cs",
                        false,
                        16,
                        1,
                        0,
                        [new WorkspaceFileWritePreviewLine(1, "add", "class Program {}")],
                        0),
                    new WorkspaceFileEditTransaction(
                        "file_write (ToDoApp/Program.cs)",
                        [new WorkspaceFileEditState("ToDoApp/Program.cs", exists: false, content: null)],
                        [new WorkspaceFileEditState("ToDoApp/Program.cs", exists: true, content: "class Program {}")])));

            FileWriteTool fileWriteTool = new(workspaceFileService.Object);
            ToolResult writeResult = await fileWriteTool.ExecuteAsync(
                CreateFileWriteContext("""{ "path": "Program.cs", "content": "class Program {}" }""", session),
                CancellationToken.None);

            writeResult.Status.Should().Be(ToolResultStatus.Success);
            workspaceFileService.VerifyAll();
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_ForwardSandboxEscalationArguments_When_Provided()
    {
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ExecuteAsync(
                It.Is<ShellCommandExecutionRequest>(request =>
                    request.Command == "dotnet test" &&
                    request.SandboxPermissions == ShellCommandSandboxPermissions.RequireEscalated &&
                    request.Justification == "needs package cache access" &&
                    request.PrefixRule != null &&
                    request.PrefixRule.SequenceEqual(new[] { "dotnet", "test" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellCommandExecutionResult(
                "dotnet test",
                ".",
                0,
                "Passed!",
                string.Empty,
                "require_escalated",
                "needs package cache access"));

        ShellCommandTool sut = new(shellCommandService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "command": "dotnet test",
                  "sandbox_permissions": "require_escalated",
                  "justification": "needs package cache access",
                  "prefix_rule": ["dotnet", "test"]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.RenderPayload!.Text.Should().Contain("Sandbox permissions: require_escalated");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ForwardPseudoTerminal_When_Requested()
    {
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ExecuteAsync(
                It.Is<ShellCommandExecutionRequest>(request =>
                    request.Command == "npm test" &&
                    request.PseudoTerminal),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellCommandExecutionResult(
                "npm test",
                ".",
                0,
                "Passed!",
                string.Empty,
                PseudoTerminal: true));

        ShellCommandTool sut = new(shellCommandService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "command": "npm test", "pty": true }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.RenderPayload!.Text.Should().Contain("Pseudo terminal: True");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ExplainUnsupportedSandboxFallback()
    {
        Mock<IShellCommandService> shellCommandService = new(MockBehavior.Strict);
        shellCommandService
            .Setup(service => service.ExecuteAsync(
                It.IsAny<ShellCommandExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellCommandExecutionResult(
                "ls -F TodoApp",
                ".",
                0,
                "Program.cs",
                string.Empty,
                SandboxEnforcement: "unsupported"));

        ShellCommandTool sut = new(shellCommandService.Object);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "command": "ls -F TodoApp" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("without OS-level sandbox enforcement");
        result.RenderPayload!.Text.Should().Contain("Sandbox enforcement: unsupported");
        result.RenderPayload.Text.Should().Contain("Sandbox note:");
        result.RenderPayload.Text.Should().Contain("without OS-level sandbox enforcement");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_EscalationJustificationIsMissing()
    {
        ShellCommandTool sut = new(Mock.Of<IShellCommandService>());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "command": "dotnet test",
                  "sandbox_permissions": "require_escalated"
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("justification");
    }

    [Fact]
    public void PermissionRequirements_Should_DefineShellArgumentPolicyWithoutCommandCatalog()
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
        registration.PermissionPolicy.Shell!.CommandArgumentName.Should().Be("command");
        registration.PermissionPolicy.Shell.SandboxPermissionsArgumentName.Should().Be("sandbox_permissions");
        registration.PermissionPolicy.Shell.JustificationArgumentName.Should().Be("justification");
        registration.PermissionPolicy.Shell.PrefixRuleArgumentName.Should().Be("prefix_rule");
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            "shell_command",
            document.RootElement.Clone(),
            session ?? TestSessionFactory.Create());
    }

    private static ToolExecutionContext CreateFileWriteContext(
        string argumentsJson,
        ReplSessionContext session)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_2",
            "file_write",
            document.RootElement.Clone(),
            session);
    }
}
