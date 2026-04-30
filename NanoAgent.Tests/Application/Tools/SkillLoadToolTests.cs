using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class SkillLoadToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_LoadSkillBodyInstructions()
    {
        SkillLoadTool sut = new(new FixedSkillService(new WorkspaceSkillLoadResult(
            "dotnet",
            "Use for .NET work.",
            ".nanoagent/skills/dotnet/SKILL.md",
            "Run dotnet test after meaningful changes.",
            42,
            false)));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "name": "dotnet" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("\"Name\":\"dotnet\"");
        result.RenderPayload!.Text.Should().Be("Run dotnet test after meaningful changes.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnNotFound_When_SkillDoesNotExist()
    {
        SkillLoadTool sut = new(new FixedSkillService(null));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "name": "missing" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.NotFound);
        result.JsonResult.Should().Contain("skill_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_NameIsMissing()
    {
        SkillLoadTool sut = new(new FixedSkillService(null));

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.JsonResult.Should().Contain("missing_skill_name");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_skill",
            AgentToolNames.SkillLoad,
            document.RootElement.Clone(),
            new ReplSessionContext(
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
                "gpt-5-mini",
                ["gpt-5-mini"]));
    }

    private sealed class FixedSkillService : ISkillService
    {
        private readonly WorkspaceSkillLoadResult? _result;

        public FixedSkillService(WorkspaceSkillLoadResult? result)
        {
            _result = result;
        }

        public Task<IReadOnlyList<WorkspaceSkillDescriptor>> ListAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WorkspaceSkillDescriptor>>([]);
        }

        public Task<string?> CreateRoutingPromptAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<WorkspaceSkillLoadResult?> LoadAsync(
            ReplSessionContext session,
            string name,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }
}
