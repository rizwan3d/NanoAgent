using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Services;

public sealed class AgentTurnServiceTests
{
    [Fact]
    public async Task RunTurnAsync_Should_RunNormalPromptThroughConversationPipeline()
    {
        ReplSessionContext session = CreateSession();
        RecordingProgressSink progressSink = new();

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "inspect this",
                session,
                progressSink,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage("Done."));

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "inspect this", progressSink),
            CancellationToken.None);

        result.ResponseText.Should().Be("Done.");
        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.BuildName);
        conversationPipeline.VerifyAll();
    }

    [Fact]
    public async Task RunTurnAsync_Should_ForwardAttachmentsToConversationPipeline()
    {
        ReplSessionContext session = CreateSession();
        RecordingProgressSink progressSink = new();
        ConversationAttachment[] attachments =
        [
            new ConversationAttachment("notes.txt", "text/plain", "bm90ZXM=", "notes")
        ];

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "inspect this",
                session,
                progressSink,
                It.Is<IReadOnlyList<ConversationAttachment>>(items =>
                    items.Count == 1 &&
                    items[0].Name == "notes.txt"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversationTurnResult.AssistantMessage("Done."));

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "inspect this", progressSink, attachments),
            CancellationToken.None);

        result.ResponseText.Should().Be("Done.");
        conversationPipeline.VerifyAll();
    }

    [Fact]
    public async Task RunTurnAsync_Should_RunDirectShellCommand_When_InputStartsWithBang()
    {
        ReplSessionContext session = CreateSession();
        RecordingProgressSink progressSink = new();
        RecordingShellCommandService shellCommandService = new(
            new ShellCommandExecutionResult(
                "dotnet --info",
                ".",
                0,
                "SDK info",
                string.Empty));
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver(),
            shellCommandService);

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "! dotnet --info", progressSink),
            CancellationToken.None);

        result.Kind.Should().Be(ConversationTurnResultKind.ToolExecution);
        result.ResponseText.Should().Contain("Shell command: dotnet --info");
        result.ResponseText.Should().Contain("SDK info");
        shellCommandService.Requests.Should().ContainSingle();
        shellCommandService.Requests[0].Command.Should().Be("dotnet --info");
        shellCommandService.Requests[0].WorkingDirectory.Should().Be(".");
        shellCommandService.Requests[0].SandboxPermissions.Should().Be(ShellCommandSandboxPermissions.RequireEscalated);
        shellCommandService.Requests[0].Justification.Should().Be("User-entered direct shell command.");
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunTurnAsync_Should_ReturnHelpfulMessage_When_DirectShellCommandIsEmpty()
    {
        ReplSessionContext session = CreateSession();
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        RecordingShellCommandService shellCommandService = new(
            new ShellCommandExecutionResult(
                string.Empty,
                ".",
                0,
                string.Empty,
                string.Empty));
        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver(),
            shellCommandService);

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "!", new RecordingProgressSink()),
            CancellationToken.None);

        result.ResponseText.Should().Be("Enter a shell command after !.");
        shellCommandService.Requests.Should().BeEmpty();
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunTurnAsync_Should_UpdateWorkingDirectory_When_DirectShellCommandIsSuccessfulCd()
    {
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-direct-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
        try
        {
            ReplSessionContext session = CreateSession(workspaceRoot);
            RecordingShellCommandService shellCommandService = new(
                new ShellCommandExecutionResult(
                    "cd src",
                    ".",
                    0,
                    string.Empty,
                    string.Empty));
            Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
            AgentTurnService sut = new(
                conversationPipeline.Object,
                new BuiltInAgentProfileResolver(),
                shellCommandService);

            ConversationTurnResult result = await sut.RunTurnAsync(
                new AgentTurnRequest(session, "!cd src", new RecordingProgressSink()),
                CancellationToken.None);

            session.WorkingDirectory.Should().Be("src");
            result.ResponseText.Should().Contain("Session working directory is now 'src'.");
            conversationPipeline.VerifyNoOtherCalls();
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
    public async Task RunTurnAsync_Should_InvokeMentionedSubagentForOneTurnAndRestoreProfile()
    {
        ReplSessionContext session = CreateSession();
        RecordingProgressSink progressSink = new();

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                "find the parser",
                session,
                progressSink,
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>((_, activeSession, _, _) =>
            {
                activeSession.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.ExploreName);
                return Task.FromResult(ConversationTurnResult.AssistantMessage("Parser is in ReplCommandParser.cs."));
            });

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "@explore find the parser", progressSink),
            CancellationToken.None);

        result.ResponseText.Should().Be("Parser is in ReplCommandParser.cs.");
        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.BuildName);
        conversationPipeline.VerifyAll();
    }

    [Fact]
    public async Task RunTurnAsync_Should_ReturnHelpfulMessage_When_MentionedAgentIsPrimary()
    {
        ReplSessionContext session = CreateSession();
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "@plan inspect this", new RecordingProgressSink()),
            CancellationToken.None);

        result.ResponseText.Should().Contain("primary profile");
        result.ResponseText.Should().Contain("/profile plan");
        conversationPipeline.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunTurnAsync_Should_ReturnHelpfulMessage_When_SubagentTaskIsMissing()
    {
        ReplSessionContext session = CreateSession();
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        AgentTurnService sut = new(
            conversationPipeline.Object,
            new BuiltInAgentProfileResolver());

        ConversationTurnResult result = await sut.RunTurnAsync(
            new AgentTurnRequest(session, "@general", new RecordingProgressSink()),
            CancellationToken.None);

        result.ResponseText.Should().Contain("Tell '@general' what to do");
        conversationPipeline.VerifyNoOtherCalls();
    }

    private static ReplSessionContext CreateSession(string? workspacePath = null)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            workspacePath: workspacePath);
    }

    private sealed class RecordingShellCommandService : IShellCommandService
    {
        private readonly ShellCommandExecutionResult _result;

        public RecordingShellCommandService(ShellCommandExecutionResult result)
        {
            _result = result;
        }

        public List<ShellCommandExecutionRequest> Requests { get; } = [];

        public Task<ShellCommandExecutionResult> ExecuteAsync(
            ShellCommandExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_result);
        }

        public Task<ShellCommandExecutionResult> StartBackgroundAsync(
            ShellCommandExecutionRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ShellCommandExecutionResult> ReadBackgroundAsync(
            string terminalId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ShellCommandExecutionResult> StopBackgroundAsync(
            string terminalId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingProgressSink : IConversationProgressSink
    {
        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
