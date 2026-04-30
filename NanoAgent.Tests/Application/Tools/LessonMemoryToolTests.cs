using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class LessonMemoryToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SaveAndSearchLessons()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService service = CreateService(workspace.Path);
        LessonMemoryTool sut = new(service);

        ToolResult saveResult = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "action": "save",
                  "trigger": "CS0246 during dotnet build",
                  "problem": "Dependency injection registration was missing",
                  "lesson": "Check ServiceCollectionExtensions before changing unrelated code.",
                  "tags": ["build", "csharp", "CS0246"]
                }
                """),
            CancellationToken.None);

        saveResult.Status.Should().Be(ToolResultStatus.Success);
        File.Exists(System.IO.Path.Combine(workspace.Path, ".nanoagent", "memory", "lessons.jsonl"))
            .Should()
            .BeTrue();

        ToolResult searchResult = await sut.ExecuteAsync(
            CreateContext("""{ "action": "search", "query": "fix build CS0246" }"""),
            CancellationToken.None);

        searchResult.Status.Should().Be(ToolResultStatus.Success);
        searchResult.RenderPayload!.Text.Should().Contain("ServiceCollectionExtensions");
        LessonMemoryToolResult payload = Deserialize(searchResult);
        payload.Count.Should().Be(1);
        payload.Lessons[0].Tags.Should().Contain("csharp");
    }

    [Fact]
    public async Task ExecuteAsync_Should_EditAndDeleteLessons()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        WorkspaceLessonMemoryService service = CreateService(workspace.Path);
        LessonMemoryTool sut = new(service);

        ToolResult saveResult = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "action": "save",
                  "trigger": "failing npm test",
                  "problem": "Mock setup was incomplete",
                  "lesson": "Check existing test doubles first.",
                  "tags": ["test"]
                }
                """),
            CancellationToken.None);
        string id = Deserialize(saveResult).Lessons[0].Id;

        ToolResult editResult = await sut.ExecuteAsync(
            CreateContext(
                $$"""
                {
                  "action": "edit",
                  "id": "{{id}}",
                  "lesson": "Check existing test doubles and shared fixtures first.",
                  "tags": ["test", "fixtures"]
                }
                """),
            CancellationToken.None);

        editResult.Status.Should().Be(ToolResultStatus.Success);
        editResult.RenderPayload!.Text.Should().Contain("shared fixtures");

        ToolResult deleteResult = await sut.ExecuteAsync(
            CreateContext($$"""{ "action": "delete", "id": "{{id}}" }"""),
            CancellationToken.None);

        deleteResult.Status.Should().Be(ToolResultStatus.Success);
        ToolResult listResult = await sut.ExecuteAsync(
            CreateContext("""{ "action": "list" }"""),
            CancellationToken.None);
        Deserialize(listResult).Count.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnNotFound_When_DeleteIdDoesNotExist()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        LessonMemoryTool sut = new(CreateService(workspace.Path));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "delete", "id": "les_missing" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.NotFound);
        result.Message.Should().Contain("les_missing");
    }

    private static WorkspaceLessonMemoryService CreateService(string workspacePath)
    {
        return new WorkspaceLessonMemoryService(
            new FixedWorkspaceRootProvider(workspacePath),
            TimeProvider.System);
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.LessonMemory,
            document.RootElement.Clone(),
            new ReplSessionContext(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
                "gpt-5-mini",
                ["gpt-5-mini"]));
    }

    private static LessonMemoryToolResult Deserialize(ToolResult result)
    {
        return JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.LessonMemoryToolResult)!;
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
