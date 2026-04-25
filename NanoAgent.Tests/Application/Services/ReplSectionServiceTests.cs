using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace NanoAgent.Tests.Application.Services;

public sealed class ReplSectionServiceTests
{
    [Fact]
    public async Task CreateNewAsync_ThenBackgroundTitleGeneration_Should_PersistGeneratedTitle()
    {
        ConversationSectionSnapshot? initialSnapshot = null;
        ConversationSectionSnapshot? titledSnapshot = null;
        int saveCount = 0;

        Mock<IConversationSectionStore> sectionStore = new(MockBehavior.Strict);
        sectionStore
            .Setup(store => store.SaveAsync(It.IsAny<ConversationSectionSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns<ConversationSectionSnapshot, CancellationToken>((snapshot, _) =>
            {
                saveCount++;

                if (saveCount == 1)
                {
                    initialSnapshot = snapshot;
                }
                else
                {
                    titledSnapshot = snapshot;
                }

                return Task.CompletedTask;
            });

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-key");

        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        providerClient
            .Setup(client => client.SendAsync(
                It.Is<ConversationProviderRequest>(request =>
                    request.ModelId == "gpt-5-mini" &&
                    request.SystemPrompt!.Contains("Capture the main engineering task, bug, feature, or subsystem") &&
                    request.Messages.Count == 1 &&
                    request.Messages[0].Content == "build a todo app" &&
                    request.AvailableTools.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationProviderPayload(
                ProviderKind.OpenAiCompatible,
                "{\"ok\":true}",
                "resp_1"));

        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);
        responseMapper
            .Setup(mapper => mapper.Map(It.IsAny<ConversationProviderPayload>()))
            .Returns(new ConversationResponse(
                "Todo App Builder",
                [],
                "resp_1"));

        ReplSectionService sut = new(
            sectionStore.Object,
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            new BuiltInAgentProfileResolver(),
            new FixedTimeProvider(new DateTimeOffset(2026, 4, 21, 2, 0, 0, TimeSpan.Zero)),
            NullLogger<ReplSectionService>.Instance);

        ReplSessionContext session = await sut.CreateNewAsync(
            "NanoAgent",
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            BuiltInAgentProfiles.Build,
            CancellationToken.None);

        sut.EnsureTitleGenerationStarted(session, "build a todo app");
        await sut.StopAsync(session, CancellationToken.None);

        saveCount.Should().Be(2);
        initialSnapshot.Should().NotBeNull();
        titledSnapshot.Should().NotBeNull();
        initialSnapshot!.Title.Should().Be(ReplSessionContext.DefaultSectionTitle);
        initialSnapshot.WorkspacePath.Should().Be(Path.GetFullPath(Directory.GetCurrentDirectory()));
        titledSnapshot!.Title.Should().Be("Todo App Builder");
        titledSnapshot.SectionId.Should().Be(session.SectionId);
        titledSnapshot.WorkspacePath.Should().Be(initialSnapshot.WorkspacePath);
        session.SectionTitle.Should().Be("Todo App Builder");
    }

    [Fact]
    public async Task ResumeAsync_Should_RestorePersistedConversationState()
    {
        ConversationSectionSnapshot snapshot = new(
            Guid.NewGuid().ToString("D"),
            "Todo App Session",
            new DateTimeOffset(2026, 4, 21, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 1, 5, 0, TimeSpan.Zero),
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            [new ConversationSectionTurn("first prompt", "first reply")],
            19,
            new PendingExecutionPlan(
                "plan the todo app",
                "Plan\n1. Inspect\n2. Implement\n3. Validate",
                ["Inspect", "Implement", "Validate"]),
            reasoningEffort: "on");

        Mock<IConversationSectionStore> sectionStore = new(MockBehavior.Strict);
        sectionStore
            .Setup(store => store.LoadAsync(snapshot.SectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        Mock<IConversationProviderClient> providerClient = new(MockBehavior.Strict);
        Mock<IConversationResponseMapper> responseMapper = new(MockBehavior.Strict);

        ReplSectionService sut = new(
            sectionStore.Object,
            secretStore.Object,
            providerClient.Object,
            responseMapper.Object,
            new BuiltInAgentProfileResolver(),
            TimeProvider.System,
            NullLogger<ReplSectionService>.Instance);

        ReplSessionContext session = await sut.ResumeAsync(
            "NanoAgent",
            snapshot.SectionId,
            profileOverride: null,
            CancellationToken.None);

        session.SectionId.Should().Be(snapshot.SectionId);
        session.SectionTitle.Should().Be("Todo App Session");
        session.IsResumedSection.Should().BeTrue();
        session.ActiveModelId.Should().Be("gpt-5-mini");
        session.AvailableModelIds.Should().Equal("gpt-5-mini", "gpt-4.1");
        session.TotalEstimatedOutputTokens.Should().Be(19);
        session.ReasoningEffort.Should().Be("on");
        session.ConversationHistory.Should().HaveCount(2);
        session.ConversationHistory[0].Content.Should().Be("first prompt");
        session.ConversationHistory[1].Content.Should().Be("first reply");
        session.PendingExecutionPlan.Should().NotBeNull();
        session.PendingExecutionPlan!.Tasks.Should().Equal("Inspect", "Implement", "Validate");
    }

    [Fact]
    public async Task ResumeAsync_Should_RestoreWorkspaceAgentProfile()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string agentsDirectory = Path.Combine(workspace.Path, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(
            Path.Combine(agentsDirectory, "qa-agent.md"),
            """
            ---
            name: qa-agent
            mode: subagent
            description: Runs validation.
            ---
            Validate the delegated change.
            """);

        ConversationSectionSnapshot snapshot = new(
            Guid.NewGuid().ToString("D"),
            "Validation Session",
            new DateTimeOffset(2026, 4, 21, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 1, 5, 0, TimeSpan.Zero),
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"],
            [],
            0,
            agentProfileName: "qa-agent");

        Mock<IConversationSectionStore> sectionStore = new(MockBehavior.Strict);
        sectionStore
            .Setup(store => store.LoadAsync(snapshot.SectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        ReplSectionService sut = new(
            sectionStore.Object,
            Mock.Of<IApiKeySecretStore>(),
            Mock.Of<IConversationProviderClient>(),
            Mock.Of<IConversationResponseMapper>(),
            new BuiltInAgentProfileResolver(new FixedWorkspaceRootProvider(workspace.Path)),
            TimeProvider.System,
            NullLogger<ReplSectionService>.Instance);

        ReplSessionContext session = await sut.ResumeAsync(
            "NanoAgent",
            snapshot.SectionId,
            profileOverride: null,
            CancellationToken.None);

        session.AgentProfile.Name.Should().Be("qa-agent");
        session.AgentProfile.SystemPrompt.Should().Contain("Validate the delegated change");
    }

    [Fact]
    public async Task ResumeAsync_Should_ClearLegacyThinkingValues_FromSavedSnapshot()
    {
        ConversationSectionSnapshot snapshot = new(
            Guid.NewGuid().ToString("D"),
            "Todo App Session",
            new DateTimeOffset(2026, 4, 21, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 1, 5, 0, TimeSpan.Zero),
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            [new ConversationSectionTurn("first prompt", "first reply")],
            19,
            reasoningEffort: "high");

        Mock<IConversationSectionStore> sectionStore = new(MockBehavior.Strict);
        sectionStore
            .Setup(store => store.LoadAsync(snapshot.SectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        ReplSectionService sut = new(
            sectionStore.Object,
            Mock.Of<IApiKeySecretStore>(),
            Mock.Of<IConversationProviderClient>(),
            Mock.Of<IConversationResponseMapper>(),
            new BuiltInAgentProfileResolver(),
            TimeProvider.System,
            NullLogger<ReplSectionService>.Instance);

        ReplSessionContext session = await sut.ResumeAsync(
            "NanoAgent",
            snapshot.SectionId,
            profileOverride: null,
            CancellationToken.None);

        session.ReasoningEffort.Should().BeNull();
    }

    [Fact]
    public async Task ResumeAsync_WhenWorkspacePathDoesNotMatchCurrentDirectory_Should_Throw()
    {
        string sectionWorkspacePath = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-OtherWorkspace-{Guid.NewGuid():N}");
        ConversationSectionSnapshot snapshot = new(
            Guid.NewGuid().ToString("D"),
            "Todo App Session",
            new DateTimeOffset(2026, 4, 21, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 1, 5, 0, TimeSpan.Zero),
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            [new ConversationSectionTurn("first prompt", "first reply")],
            19,
            workspacePath: sectionWorkspacePath);

        Mock<IConversationSectionStore> sectionStore = new(MockBehavior.Strict);
        sectionStore
            .Setup(store => store.LoadAsync(snapshot.SectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        ReplSectionService sut = new(
            sectionStore.Object,
            Mock.Of<IApiKeySecretStore>(),
            Mock.Of<IConversationProviderClient>(),
            Mock.Of<IConversationResponseMapper>(),
            new BuiltInAgentProfileResolver(),
            TimeProvider.System,
            NullLogger<ReplSectionService>.Instance);

        Func<Task> act = () => sut.ResumeAsync(
            "NanoAgent",
            snapshot.SectionId,
            profileOverride: null,
            CancellationToken.None);

        SectionWorkspaceMismatchException exception = (await act.Should()
            .ThrowAsync<SectionWorkspaceMismatchException>())
            .Which;
        exception.Message.Should().StartWith(SectionWorkspaceMismatchException.DefaultMessage);
        exception.SectionWorkspacePath.Should().Be(Path.GetFullPath(sectionWorkspacePath));
        exception.CurrentWorkspacePath.Should().Be(Path.GetFullPath(Directory.GetCurrentDirectory()));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
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
                "nanoagent-section-profile-tests-" + Guid.NewGuid().ToString("N"));
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
