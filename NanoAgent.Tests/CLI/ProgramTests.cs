using FluentAssertions;
using NanoAgent.CLI;
using NanoAgent.Application.Backend;
using Moq;
using NanoAgent.Infrastructure.WindowsSandbox;
using Spectre.Console;
using System.Reflection;

namespace NanoAgent.Tests.CLI;

public sealed class ProgramTests
{
    [Fact]
    public void TryHandleWindowsSandboxSpecialInvocation_Should_ReturnFalse_ForRegularCliArgs()
    {
        bool handled = Program.TryHandleWindowsSandboxSpecialInvocation(
            ["--interactive"],
            out int exitCode);

        handled.Should().BeFalse();
        exitCode.Should().Be(0);
    }

    [Fact]
    public void TryHandleWindowsSandboxSpecialInvocation_Should_ReturnUsageError_WhenSetupPayloadIsMissing()
    {
        bool handled = Program.TryHandleWindowsSandboxSpecialInvocation(
            [WindowsSandboxSetupOrchestrator.SetupCommandArgument],
            out int exitCode);

        handled.Should().BeTrue();
        exitCode.Should().Be(2);
    }

    [Fact]
    public void RenderSessionView_Should_ClearPinnedPlanState()
    {
        AppState state = new(
            new UiBridge(),
            new Mock<INanoAgentBackend>(MockBehavior.Strict).Object)
        {
            IsPlanPinned = true,
            LatestPlanText = "Plan progress: 1/3"
        };

        BackendSessionInfo sessionInfo = new(
            SessionId: "session-id",
            SectionResumeCommand: "/resume session-id",
            ProviderName: "provider",
            ModelId: "model",
            ActiveModelContextWindowTokens: 1234,
            AvailableModelIds: [],
            ThinkingMode: "default",
            AgentProfileName: "agent",
            SectionTitle: "title",
            IsResumedSection: false,
            ConversationHistory: []);

        MethodInfo renderSessionView = typeof(Program).GetMethod(
            "RenderSessionView",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        renderSessionView.Invoke(null, [state, sessionInfo, null]);

        state.IsPlanPinned.Should().BeFalse();
        state.LatestPlanText.Should().BeNull();
    }

    [Fact]
    public void BuildUi_Should_FallBackToCompactPanel_When_TerminalIsTooSmall()
    {
        AppState state = new(
            new UiBridge(),
            new Mock<INanoAgentBackend>(MockBehavior.Strict).Object);

        MethodInfo buildUi = typeof(Program).GetMethod(
            "BuildUi",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(AppState), typeof(int), typeof(int)],
            modifiers: null)!;

        object? renderable = buildUi.Invoke(null, [state, 18, 9]);

        renderable.Should().BeOfType<Panel>();
    }

    [Fact]
    public void BuildUi_Should_FallBackToCompactPanel_When_PinnedPlanCannotFit()
    {
        AppState state = new(
            new UiBridge(),
            new Mock<INanoAgentBackend>(MockBehavior.Strict).Object)
        {
            IsPlanPinned = true,
            LatestPlanText = "Plan progress: 1/3"
        };

        MethodInfo buildUi = typeof(Program).GetMethod(
            "BuildUi",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(AppState), typeof(int), typeof(int)],
            modifiers: null)!;

        object? renderable = buildUi.Invoke(null, [state, 80, 12]);

        renderable.Should().BeOfType<Panel>();
    }
}
