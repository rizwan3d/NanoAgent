using FluentAssertions;
using NanoAgent.Application.Models;
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
            LatestPlanProgress = new ExecutionPlanProgress(["Inspect context"], 1),
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
            ReasoningEffort: null,
            ShowThinking: false,
            AgentProfileName: "agent",
            SectionTitle: "title",
            IsResumedSection: false,
            ConversationHistory: []);

        MethodInfo renderSessionView = typeof(Program).GetMethod(
            "RenderSessionView",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        renderSessionView.Invoke(null, [state, sessionInfo, null]);

        state.IsPlanPinned.Should().BeFalse();
        state.LatestPlanProgress.Should().BeNull();
        state.LatestPlanText.Should().BeNull();
    }

    [Fact]
    public void StartConversation_Should_ClearCompletedPlanState()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsReady = true,
            IsPlanPinned = true,
            LatestPlanProgress = new ExecutionPlanProgress(["Inspect", "Patch"], 2),
            LatestPlanText = "Plan progress: 2/2"
        };

        MethodInfo startConversation = typeof(Program).GetMethod(
            "StartConversation",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        startConversation.Invoke(null, [state, "Ship it", null]);

        state.IsPlanPinned.Should().BeFalse();
        state.LatestPlanProgress.Should().BeNull();
        state.LatestPlanText.Should().BeNull();
        state.IsBusy.Should().BeTrue();
        state.ActiveOperation.Should().NotBeNull();
    }

    [Fact]
    public void StartConversation_Should_KeepIncompletePlanState()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsReady = true,
            IsPlanPinned = true,
            LatestPlanProgress = new ExecutionPlanProgress(["Inspect", "Patch"], 1),
            LatestPlanText = "Plan progress: 1/2"
        };

        MethodInfo startConversation = typeof(Program).GetMethod(
            "StartConversation",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        startConversation.Invoke(null, [state, "Continue", null]);

        state.IsPlanPinned.Should().BeTrue();
        state.LatestPlanProgress.Should().NotBeNull();
        state.LatestPlanText.Should().Be("Plan progress: 1/2");
        state.IsBusy.Should().BeTrue();
        state.ActiveOperation.Should().NotBeNull();
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

    [Fact]
    public void SubmitInput_Should_QueuePrompt_When_Busy()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsReady = true,
            IsBusy = true,
            InputCursorIndex = "Queued prompt".Length
        };
        state.Input.Append("Queued prompt");

        MethodInfo submitInput = typeof(Program).GetMethod(
            "SubmitInput",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        submitInput.Invoke(null, [state]);

        state.PendingSubmissions.Should().ContainSingle();
        state.PendingSubmissions.Peek().Kind.Should().Be(PendingSubmissionKind.Prompt);
        state.PendingSubmissions.Peek().Text.Should().Be("Queued prompt");
        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.System &&
            message.Text.Contains("Queued prompt: Queued prompt"));
        state.Input.ToString().Should().BeEmpty();
    }

    [Fact]
    public void SubmitInput_Should_NotQueueCommand_When_Busy()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsReady = true,
            IsBusy = true,
            InputCursorIndex = "/help".Length
        };
        state.Input.Append("/help");

        MethodInfo submitInput = typeof(Program).GetMethod(
            "SubmitInput",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        submitInput.Invoke(null, [state]);

        state.PendingSubmissions.Should().BeEmpty();
        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.System &&
            message.Text.Contains("That command is unavailable while NanoAgent is working."));
    }

    [Fact]
    public void SubmitInput_Should_PreserveInput_When_BackendIsNotReady()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            InputCursorIndex = "Wait for startup".Length
        };
        state.Input.Append("Wait for startup");

        MethodInfo submitInput = typeof(Program).GetMethod(
            "SubmitInput",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        submitInput.Invoke(null, [state]);

        state.Input.ToString().Should().Be("Wait for startup");
        state.InputCursorIndex.Should().Be("Wait for startup".Length);
        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.System &&
            message.Text == "NanoAgent is still starting up. Please wait.");
    }

    [Fact]
    public async Task StartInitialization_Should_StopInteractiveSession_When_BackendStartupFails()
    {
        Mock<INanoAgentBackend> backend = new(MockBehavior.Strict);
        backend
            .Setup(static value => value.InitializeAsync(
                It.IsAny<NanoAgent.Application.UI.IUiBridge>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        AppState state = new(new UiBridge(), backend.Object);

        MethodInfo startInitialization = typeof(Program).GetMethod(
            "StartInitialization",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        startInitialization.Invoke(null, [state]);
        await state.ActiveOperation!;
        state.UiBridge.ApplyPending(state);

        state.HasFatalError.Should().BeTrue();
        state.Running.Should().BeFalse();
        state.FatalExitMessage.Should().Be("Failed to start NanoAgent: boom");
        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.System &&
            message.Text == "Failed to start NanoAgent: boom");
    }

    [Fact]
    public void GetSafePath_Should_RejectSiblingDirectoryPrefixEscape()
    {
        AppState state = new(
            new UiBridge(),
            new Mock<INanoAgentBackend>(MockBehavior.Strict).Object);
        string rootName = new DirectoryInfo(state.RootDirectory).Name;

        MethodInfo getSafePath = typeof(Program).GetMethod(
            "GetSafePath",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Action act = () => getSafePath.Invoke(
            null,
            [state, $@"..\{rootName}-copy\secret.txt"]);

        TargetInvocationException exception = act.Should().Throw<TargetInvocationException>().Which;
        exception.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Path escapes workspace.");
    }

    [Fact]
    public void TryHandleTerminalEscapeFollowupKey_Should_RequeueNonAnsiInput()
    {
        AppState state = new(
            new UiBridge(),
            new Mock<INanoAgentBackend>(MockBehavior.Strict).Object);
        MethodInfo tryHandleTerminalEscapeFollowupKey = typeof(Program).GetMethod(
            "TryHandleTerminalEscapeFollowupKey",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        object? handled = tryHandleTerminalEscapeFollowupKey.Invoke(
            null,
            [state, new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)]);

        handled.Should().Be(false);
        state.PendingInputKeys.Should().ContainSingle();
        state.PendingInputKeys.Peek().KeyChar.Should().Be('a');
    }

    [Fact]
    public void TryStartNextPendingSubmission_Should_RunQueuedPrompt_When_Idle()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsReady = true
        };
        state.PendingSubmissions.Enqueue(new PendingSubmission(
            PendingSubmissionKind.Prompt,
            "Continue with queued work"));

        MethodInfo tryStartNextPendingSubmission = typeof(Program).GetMethod(
            "TryStartNextPendingSubmission",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        tryStartNextPendingSubmission.Invoke(null, [state]);

        state.PendingSubmissions.Should().BeEmpty();
        state.IsBusy.Should().BeTrue();
        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.User &&
            message.Text == "Continue with queued work");
    }

    [Fact]
    public void HandleCommand_Should_AllowClearWhileBusy()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsBusy = true
        };
        state.AddMessage(Role.User, "Earlier message");

        MethodInfo handleCommand = typeof(Program).GetMethod(
            "HandleCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        handleCommand.Invoke(null, [state, "/clear"]);

        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.System &&
            message.Text == "Screen cleared.");
    }

    [Fact]
    public void HandleCommand_Should_BlockBackendCommandWhileBusy()
    {
        AppState state = new(new UiBridge(), CreateConversationBackend().Object)
        {
            IsBusy = true
        };

        MethodInfo handleCommand = typeof(Program).GetMethod(
            "HandleCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        handleCommand.Invoke(null, [state, "/help"]);

        state.Messages.Should().ContainSingle(message =>
            message.Role == Role.System &&
            message.Text == "That command is unavailable while NanoAgent is working.");
    }

    private static Mock<INanoAgentBackend> CreateConversationBackend()
    {
        Mock<INanoAgentBackend> backend = new(MockBehavior.Strict);
        backend
            .Setup(static value => value.RunTurnAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ConversationAttachment>>(),
                It.IsAny<NanoAgent.Application.UI.IUiBridge>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage("Done."));

        return backend;
    }
}
