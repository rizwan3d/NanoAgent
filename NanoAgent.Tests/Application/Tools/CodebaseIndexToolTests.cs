using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class CodebaseIndexToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_SearchCodebaseIndex()
    {
        RecordingCodebaseIndexService service = new();
        CodebaseIndexTool sut = new(service);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "search", "query": "service registration", "limit": 3 }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        service.SearchQuery.Should().Be("service registration");
        service.SearchLimit.Should().Be(3);
        CodebaseIndexSearchResult payload = JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.CodebaseIndexSearchResult)!;
        payload.Matches.Should().ContainSingle(match => match.Path == "src/ServiceRegistry.cs");
        result.RenderPayload!.Text.Should().Contain("src/ServiceRegistry.cs");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_SearchQueryIsMissing()
    {
        CodebaseIndexTool sut = new(new RecordingCodebaseIndexService());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "action": "search" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("query");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.CodebaseIndex,
            document.RootElement.Clone(),
            new ReplSessionContext(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
                "gpt-5-mini",
                ["gpt-5-mini"]));
    }

    private sealed class RecordingCodebaseIndexService : ICodebaseIndexService
    {
        public int SearchLimit { get; private set; }

        public string? SearchQuery { get; private set; }

        public Task<CodebaseIndexBuildResult> BuildAsync(
            bool force,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CodebaseIndexBuildResult(
                ".nanoagent/cache/codebase-index.json",
                DateTimeOffset.UtcNow,
                IndexedFileCount: 1,
                AddedFileCount: 1,
                UpdatedFileCount: 0,
                RemovedFileCount: 0,
                ReusedFileCount: 0,
                SkippedFileCount: 0,
                DurationMilliseconds: 1));
        }

        public Task<CodebaseIndexStatusResult> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new CodebaseIndexStatusResult(
                ".nanoagent/cache/codebase-index.json",
                Exists: true,
                IsStale: false,
                BuiltAtUtc: DateTimeOffset.UtcNow,
                IndexedFileCount: 1,
                WorkspaceFileCount: 1,
                NewFileCount: 0,
                ChangedFileCount: 0,
                DeletedFileCount: 0,
                SkippedFileCount: 0,
                SampleNewFiles: [],
                SampleChangedFiles: [],
                SampleDeletedFiles: []));
        }

        public Task<CodebaseIndexSearchResult> SearchAsync(
            string query,
            int limit,
            bool includeSnippets,
            CancellationToken cancellationToken)
        {
            SearchQuery = query;
            SearchLimit = limit;
            return Task.FromResult(new CodebaseIndexSearchResult(
                query,
                ".nanoagent/cache/codebase-index.json",
                IndexWasUpdated: false,
                IndexedFileCount: 1,
                Matches:
                [
                    new CodebaseIndexSearchMatch(
                        "src/ServiceRegistry.cs",
                        "csharp",
                        12.5,
                        ["ServiceRegistry"],
                        [new CodebaseIndexSnippet(4, "public sealed class ServiceRegistry")])
                ]));
        }

        public Task<CodebaseIndexListResult> ListAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CodebaseIndexListResult(
                ".nanoagent/cache/codebase-index.json",
                TotalIndexedFileCount: 1,
                ReturnedFileCount: 1,
                Files: ["src/ServiceRegistry.cs"]));
        }
    }
}
