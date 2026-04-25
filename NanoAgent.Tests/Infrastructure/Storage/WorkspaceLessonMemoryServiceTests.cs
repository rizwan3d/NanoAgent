using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Infrastructure.Storage;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class WorkspaceLessonMemoryServiceTests
{
    [Fact]
    public async Task ObserveToolResultAsync_Should_StoreShellBuildLessonOnlyAfterSuccessfulCorrection()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(workspace.Path);

        await sut.ObserveToolResultAsync(
            CreateToolCall(AgentToolNames.ShellCommand, "{}"),
            CreateShellInvocation(new ShellCommandExecutionResult(
                "dotnet build",
                ".",
                1,
                "",
                "Program.cs(10,5): error CS0246: The type or namespace name 'MissingType' could not be found.")),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> failures = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        failures.Should().BeEmpty();

        await sut.ObserveToolResultAsync(
            CreateToolCall(AgentToolNames.ShellCommand, "{}"),
            CreateShellInvocation(new ShellCommandExecutionResult(
                "dotnet build",
                ".",
                0,
                "Build succeeded.",
                "")),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> fixedFailures = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        fixedFailures.Should().ContainSingle();
        fixedFailures[0].Kind.Should().Be("lesson");
        fixedFailures[0].IsFixed.Should().BeTrue();
        fixedFailures[0].FixedAtUtc.Should().NotBeNull();
        fixedFailures[0].FailureSignature.Should().Be("CS0246");
        fixedFailures[0].Lesson.Should().Contain("corrected successful pattern");
        fixedFailures[0].FixSummary.Should().Contain("exited 0");
    }

    [Fact]
    public async Task CreatePromptAsync_Should_ReturnRelevantLessons()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(workspace.Path);
        await sut.SaveAsync(
            new LessonMemorySaveRequest(
                "CS0246 during build",
                "A service registration was missing",
                "Check DI registration before editing unrelated files.",
                ["build", "csharp", "CS0246"]),
            CancellationToken.None);

        string? prompt = await sut.CreatePromptAsync(
            "Fix the build CS0246",
            CancellationToken.None);

        prompt.Should().NotBeNull();
        prompt.Should().Contain("Relevant lesson memory");
        prompt.Should().Contain("Check DI registration");
        prompt.Should().Contain(".nanoagent/memory/lessons.jsonl");
    }

    [Fact]
    public async Task ObserveToolResultAsync_Should_RedactSecretsFromAutomaticFailureLessons()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(
            workspace.Path,
            new MemorySettings
            {
                RedactSecrets = true
            });

        await sut.ObserveToolResultAsync(
            CreateToolCall(AgentToolNames.ShellCommand, "{}"),
            CreateShellInvocation(new ShellCommandExecutionResult(
                "dotnet test",
                ".",
                1,
                "",
                "error: token=sk-abcdefghijklmnopqrstuvwxyz123456 github_pat_abcdefghijklmnopqrstuvwxyz1234567890 AIzaabcdefghijklmnopqrstuvwxyz123456")),
            CancellationToken.None);

        await sut.ObserveToolResultAsync(
            CreateToolCall(AgentToolNames.ShellCommand, "{}"),
            CreateShellInvocation(new ShellCommandExecutionResult(
                "dotnet test",
                ".",
                0,
                "Passed.",
                "")),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> lessons = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        lessons.Should().ContainSingle();
        lessons[0].FailureSignature.Should().Contain("<redacted>");
        lessons[0].FailureSignature.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz");
        lessons[0].FailureSignature.Should().NotContain("github_pat_abcdefghijklmnopqrstuvwxyz");
        lessons[0].FailureSignature.Should().NotContain("AIzaabcdefghijklmnopqrstuvwxyz");
    }

    [Fact]
    public async Task ObserveToolResultAsync_Should_StoreToolLessonOnlyAfterCorrectedSuccess()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(workspace.Path);

        await sut.ObserveToolResultAsync(
            CreateToolCall(
                AgentToolNames.ApplyPatch,
                """{ "patch": "--- a/README.md\n+++ b/README.md" }"""),
            new ToolInvocationResult(
                "call_patch_fail",
                AgentToolNames.ApplyPatch,
                ToolResultFactory.InvalidArguments(
                    "invalid_patch",
                    "Patch text must begin with '*** Begin Patch'.")),
            CancellationToken.None);

        (await sut.ListAsync(10, includeFixed: true, CancellationToken.None))
            .Should()
            .BeEmpty();

        await sut.ObserveToolResultAsync(
            CreateToolCall(
                AgentToolNames.ApplyPatch,
                """{ "patch": "*** Begin Patch\n*** Update File: README.md\n@@\n-old\n+new\n*** End Patch" }"""),
            new ToolInvocationResult(
                "call_patch_success",
                AgentToolNames.ApplyPatch,
                ToolResultFactory.Success(
                    "Applied patch to 1 file.",
                    new WorkspaceApplyPatchResult(1, 1, 1, []),
                    ToolJsonContext.Default.WorkspaceApplyPatchResult)),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> lessons = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        lessons.Should().ContainSingle();
        lessons[0].Kind.Should().Be("lesson");
        lessons[0].IsFixed.Should().BeTrue();
        lessons[0].Problem.Should().Contain("invalid_patch");
        lessons[0].Lesson.Should().Contain("corrected successful pattern");
        lessons[0].FixSummary.Should().Contain("Applied patch to 1 file");
    }

    [Fact]
    public async Task ObserveToolResultAsync_Should_IgnoreFileLocationMisses()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(workspace.Path);

        await sut.ObserveToolResultAsync(
            CreateToolCall(AgentToolNames.FileRead, """{ "path": "missing.txt" }"""),
            new ToolInvocationResult(
                "call_read_fail",
                AgentToolNames.FileRead,
                ToolResultFactory.ExecutionError(
                    "tool_execution_failed",
                    "Tool execution failed unexpectedly: File 'missing.txt' does not exist.")),
            CancellationToken.None);

        await sut.ObserveToolResultAsync(
            CreateToolCall(AgentToolNames.FileRead, """{ "path": "README.md" }"""),
            new ToolInvocationResult(
                "call_read_success",
                AgentToolNames.FileRead,
                ToolResultFactory.Success(
                    "Read file 'README.md'.",
                    new WorkspaceFileReadResult("README.md", "hello", 5),
                    ToolJsonContext.Default.WorkspaceFileReadResult)),
            CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> lessons = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        lessons.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Should_RespectMaxEntries()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService sut = CreateService(
            workspace.Path,
            new MemorySettings
            {
                MaxEntries = 2
            });

        await sut.SaveAsync(new LessonMemorySaveRequest("one", "problem one", "lesson one"), CancellationToken.None);
        await sut.SaveAsync(new LessonMemorySaveRequest("two", "problem two", "lesson two"), CancellationToken.None);
        await sut.SaveAsync(new LessonMemorySaveRequest("three", "problem three", "lesson three"), CancellationToken.None);

        IReadOnlyList<LessonMemoryEntry> lessons = await sut.ListAsync(
            limit: 10,
            includeFixed: true,
            CancellationToken.None);

        lessons.Should().HaveCount(2);
        lessons.Select(static lesson => lesson.Trigger).Should().BeEquivalentTo("two", "three");
    }

    private static ToolInvocationResult CreateShellInvocation(ShellCommandExecutionResult result)
    {
        return new ToolInvocationResult(
            "call_shell",
            AgentToolNames.ShellCommand,
            ToolResultFactory.Success(
                "shell",
                result,
                ToolJsonContext.Default.ShellCommandExecutionResult));
    }

    private static ConversationToolCall CreateToolCall(
        string toolName,
        string argumentsJson)
    {
        return new ConversationToolCall(
            $"call_{toolName}",
            toolName,
            argumentsJson);
    }

    private static WorkspaceLessonMemoryService CreateService(
        string workspacePath,
        MemorySettings? settings = null)
    {
        return new WorkspaceLessonMemoryService(
            new FixedWorkspaceRootProvider(workspacePath),
            TimeProvider.System,
            settings);
    }

    private sealed class FixedWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspacePath;

        public FixedWorkspaceRootProvider(string workspacePath)
        {
            _workspacePath = workspacePath;
        }

        public string GetWorkspaceRoot()
        {
            return _workspacePath;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorkspace Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NanoAgent.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
