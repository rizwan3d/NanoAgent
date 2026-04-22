using System.Text.Json;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Tools;

public sealed class PlanningModeToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_ObjectiveIsMissing()
    {
        PlanningModeTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'objective'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResult_When_ObjectiveIsProvided()
    {
        PlanningModeTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "objective": "Refactor the pipeline." }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("Planning mode activated");
        result.JsonResult.Should().Contain("\"Objective\":\"Refactor the pipeline.\"");
        result.JsonResult.Should().Contain("\"SuggestedResponseSections\"");
        result.JsonResult.Should().Contain("\"Verified facts\"");
        result.JsonResult.Should().Contain("\"Assumptions / open questions\"");
        result.JsonResult.Should().Contain("\"Environment / toolchain\"");
        result.JsonResult.Should().Contain("\"Candidate approaches\"");
        result.JsonResult.Should().Contain("\"Immediate next step\"");
        result.JsonResult.Should().Contain("\"Validation\"");
        result.JsonResult.Should().Contain("\"Recommended approach\"");
        result.RenderPayload.Should().NotBeNull();
        result.RenderPayload!.Title.Should().Contain("Planning mode");
        result.RenderPayload.Text.Should().Contain("Objective: Refactor the pipeline.");
        result.RenderPayload.Text.Should().Contain("Planning guidance:");
        result.RenderPayload.Text.Should().Contain("Check installed build tools, compilers, SDKs");
        result.RenderPayload.Text.Should().Contain("scaffold, build, or test commands");
        result.RenderPayload.Text.Should().Contain("project scaffolding, dependency restore/install");
        result.RenderPayload.Text.Should().Contain("Use update_plan");
        result.RenderPayload.Text.Should().Contain("one active in_progress step");
        result.RenderPayload.Text.Should().Contain("Separate verified facts from assumptions");
        result.RenderPayload.Text.Should().Contain("When multiple reasonable approaches exist");
        result.RenderPayload.Text.Should().Contain("Keep the immediate next step explicit");
        result.RenderPayload.Text.Should().Contain("high-quality ordered task list");
        result.RenderPayload.Text.Should().Contain("Avoid vague plans like");
        result.RenderPayload.Text.Should().Contain("Suggested sections:");
        result.RenderPayload.Text.Should().Contain("- Verified facts");
        result.RenderPayload.Text.Should().Contain("- Immediate next step");
        result.RenderPayload.Text.Should().Contain("Quality checklist:");
        result.RenderPayload.Text.Should().Contain("Do not produce a vague plan.");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.PlanningMode,
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
