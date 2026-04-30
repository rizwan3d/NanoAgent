using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class AgentOrchestrateToolTests
{
    [Fact]
    public void Schema_Should_ListWorkspaceSubagents()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string agentsDirectory = Path.Combine(workspace.Path, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(
            Path.Combine(agentsDirectory, "qa_agent.md"),
            """
            ---
            name: qa-agent
            mode: subagent
            description: Validates focused changes.
            ---
            Inspect validation risk and report the relevant commands.
            """);

        using ServiceProvider serviceProvider = new ServiceCollection().BuildServiceProvider();
        AgentOrchestrateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(new FixedWorkspaceRootProvider(workspace.Path)),
            new HeuristicTokenEstimator());

        using JsonDocument schema = JsonDocument.Parse(sut.Schema);
        string[] agentNames = schema.RootElement
            .GetProperty("properties")
            .GetProperty("tasks")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("agent")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => value is not null)
            .Select(static value => value!)
            .ToArray();

        agentNames.Should().Contain("general");
        agentNames.Should().Contain("explore");
        agentNames.Should().Contain("qa-agent");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RunReadOnlyTasksInParallel()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Build);
        TaskCompletionSource bothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int startedCount = 0;

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<ReplSessionContext>(),
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>(async (input, _, _, token) =>
            {
                if (Interlocked.Increment(ref startedCount) == 2)
                {
                    bothStarted.TrySetResult();
                }

                await release.Task.WaitAsync(token);
                return ConversationTurnResult.AssistantMessage(
                    input.Contains("routing", StringComparison.Ordinal)
                        ? "Routing lives in AgentConversationPipeline."
                        : "Profiles live in BuiltInAgentProfiles.",
                    metrics: new ConversationTurnMetrics(TimeSpan.FromMilliseconds(20), 4));
            });

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentOrchestrateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        Task<ToolResult> runTask = sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """
                {
                  "strategy": "parallel_readonly",
                  "tasks": [
                    { "agent": "explore", "task": "Find routing code" },
                    { "agent": "explore", "task": "Find profile code" }
                  ]
                }
                """),
            CancellationToken.None);

        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        ToolResult result = await runTask;

        result.Status.Should().Be(ToolResultStatus.Success);
        AgentOrchestrationResult payload = JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.AgentOrchestrationResult)!;
        payload.Strategy.Should().Be("parallel_readonly");
        payload.Tasks.Should().HaveCount(2);
        payload.SucceededTaskCount.Should().Be(2);
        payload.Tasks.Select(static task => task.AgentName).Should().Equal("explore", "explore");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RunEditingCapableTasksSequentiallyInAutoMode()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Build);
        int activeCount = 0;
        int maxActiveCount = 0;
        List<string> observedInputs = [];

        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);
        conversationPipeline
            .Setup(pipeline => pipeline.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<ReplSessionContext>(),
                It.IsAny<IConversationProgressSink>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ReplSessionContext, IConversationProgressSink, CancellationToken>(async (input, childSession, _, token) =>
            {
                int active = Interlocked.Increment(ref activeCount);
                maxActiveCount = Math.Max(maxActiveCount, active);
                observedInputs.Add(input);

                try
                {
                    childSession.RecordFileEditTransaction(new WorkspaceFileEditTransaction(
                        "child edit",
                        [new WorkspaceFileEditState($"src/{observedInputs.Count}.cs", true, "before")],
                        [new WorkspaceFileEditState($"src/{observedInputs.Count}.cs", true, "after")]));
                    await Task.Delay(25, token);
                    return ConversationTurnResult.AssistantMessage("Completed focused edit.");
                }
                finally
                {
                    Interlocked.Decrement(ref activeCount);
                }
            });

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentOrchestrateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """
                {
                  "tasks": [
                    { "agent": "general", "task": "Edit parser", "writeScope": "NanoAgent/Application/Commands" },
                    { "agent": "general", "task": "Edit tests", "writeScope": "NanoAgent.Tests/Application/Commands" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        maxActiveCount.Should().Be(1);
        observedInputs[0].Should().Contain("Edit parser");
        observedInputs[0].Should().Contain("Write scope:");
        observedInputs[1].Should().Contain("Edit tests");
        parentSession.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? transaction)
            .Should()
            .BeTrue();
        transaction.Should().NotBeNull();

        AgentOrchestrationResult payload = JsonSerializer.Deserialize(
            result.JsonResult,
            ToolJsonContext.Default.AgentOrchestrationResult)!;
        payload.RecordedFileEdits.Should().BeTrue();
        payload.Tasks.Should().OnlyContain(static task => task.RecordedFileEdits);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DenyEditingSubagent_When_ParentProfileIsReadOnly()
    {
        ReplSessionContext parentSession = CreateSession(BuiltInAgentProfiles.Plan);
        Mock<IConversationPipeline> conversationPipeline = new(MockBehavior.Strict);

        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(conversationPipeline.Object)
            .BuildServiceProvider();
        AgentOrchestrateTool sut = new(
            serviceProvider,
            new BuiltInAgentProfileResolver(),
            new HeuristicTokenEstimator());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                parentSession,
                """
                {
                  "tasks": [
                    { "agent": "general", "task": "Implement a fix" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.PermissionDenied);
        result.JsonResult.Should().Contain("readonly_profile_cannot_orchestrate_edits");
        conversationPipeline.VerifyNoOtherCalls();
    }

    private static ToolExecutionContext CreateContext(
        ReplSessionContext session,
        string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_agents",
            AgentToolNames.AgentOrchestrate,
            document.RootElement.Clone(),
            session);
    }

    private static ReplSessionContext CreateSession(IAgentProfile profile)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            agentProfile: profile);
    }

    private sealed class FixedWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspaceRoot;

        public FixedWorkspaceRootProvider(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public string GetWorkspaceRoot()
        {
            return _workspaceRoot;
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
                "nanoagent-agent-orchestrate-tests-" + Guid.NewGuid().ToString("N"));
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
