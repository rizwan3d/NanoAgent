using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class UpdatePlanToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_PlanIsMissing()
    {
        UpdatePlanTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'plan' array");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnStructuredResult_When_PlanIsValid()
    {
        UpdatePlanTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "explanation": "Need a visible task list.",
                  "plan": [
                    { "step": "Inspect current planning flow", "status": "completed" },
                    { "step": "Add update_plan support", "status": "in_progress" },
                    { "step": "Run validation", "status": "pending" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("1 completed, 1 in progress, 1 pending");
        result.JsonResult.Should().Contain("\"Step\":\"Add update_plan support\"");
        result.JsonResult.Should().Contain("\"Status\":\"in_progress\"");
        result.JsonResult.Should().Contain("\"CompletedTaskCount\":1");
        result.RenderPayload.Should().NotBeNull();
        result.RenderPayload!.Text.Should().Contain("Need a visible task list.");
        result.RenderPayload.Text.Should().Contain("\u2713 Inspect current planning flow");
        result.RenderPayload.Text.Should().Contain("\u2610 Add update_plan support");
        result.RenderPayload.Text.Should().Contain("\u2610 Run validation");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_MultipleStepsAreInProgress()
    {
        UpdatePlanTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "plan": [
                    { "step": "Implement one slice", "status": "in_progress" },
                    { "step": "Implement another slice", "status": "in_progress" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("Only one update_plan item can be in_progress");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_StatusesAreOutOfOrder()
    {
        UpdatePlanTool sut = new();

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "plan": [
                    { "step": "Run validation", "status": "pending" },
                    { "step": "Inspect files", "status": "completed" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("completed steps first");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.UpdatePlan,
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }
}
