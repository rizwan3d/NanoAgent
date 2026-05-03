using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class RepoMemoryToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ListReadAndWriteRepoMemoryDocuments()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        RepoMemoryTool sut = new();

        ToolResult listResult = await sut.ExecuteAsync(
            CreateContext(workspace.Path, """{ "action": "list" }"""),
            CancellationToken.None);

        listResult.Status.Should().Be(ToolResultStatus.Success);
        RepoMemoryToolResult listPayload = Deserialize(listResult);
        listPayload.Documents.Should().HaveCount(5);
        listPayload.Documents.Should().Contain(document => document.Name == "architecture");

        ToolResult writeResult = await sut.ExecuteAsync(
            CreateContext(
                workspace.Path,
                """
                {
                  "action": "write",
                  "document": "architecture",
                  "content": "# Architecture\n\nNanoAgent uses an application layer and an infrastructure layer."
                }
                """),
            CancellationToken.None);

        writeResult.Status.Should().Be(ToolResultStatus.Success);
        File.ReadAllText(Path.Combine(workspace.Path, ".nanoagent", "memory", "architecture.md"))
            .Should()
            .Contain("application layer");

        ToolResult readResult = await sut.ExecuteAsync(
            CreateContext(workspace.Path, """{ "action": "read", "document": "architecture" }"""),
            CancellationToken.None);

        readResult.Status.Should().Be(ToolResultStatus.Success);
        readResult.RenderPayload!.Text.Should().Contain("application layer");
        RepoMemoryToolResult readPayload = Deserialize(readResult);
        readPayload.Documents[0].Path.Should().Be(".nanoagent/memory/architecture.md");
        readPayload.Documents[0].Content.Should().Contain("NanoAgent uses");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RecordFileEditTransactionForWrites()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        ReplSessionContext session = CreateSession(workspace.Path);
        RepoMemoryTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                session,
                """
                {
                  "action": "write",
                  "document": "test-strategy",
                  "content": "# Test Strategy\n\nRun dotnet test."
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction).Should().BeTrue();
        transaction!.Description.Should().Contain("repo_memory");
        transaction.AfterStates.Should().ContainSingle(state =>
            state.Path == ".nanoagent/memory/test-strategy.md" &&
            state.Content!.Contains("dotnet test", StringComparison.Ordinal));
    }

    private static ToolExecutionContext CreateContext(
        string workspacePath,
        string argumentsJson)
    {
        return CreateContext(
            CreateSession(workspacePath),
            argumentsJson);
    }

    private static ToolExecutionContext CreateContext(
        ReplSessionContext session,
        string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.RepoMemory,
            document.RootElement.Clone(),
            session);
    }

    private static ReplSessionContext CreateSession(string workspacePath)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            workspacePath: workspacePath);
    }

    private static RepoMemoryToolResult Deserialize(ToolResult result)
    {
        return JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.RepoMemoryToolResult)!;
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
