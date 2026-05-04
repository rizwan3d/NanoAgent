using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class SessionCommandHandlerTests : IDisposable
{
    private readonly string _tempDirectory;

    public SessionCommandHandlerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "nanoagent-session-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ExportAsync_Should_WriteJsonExport()
    {
        ReplSessionContext session = CreateSession();
        session.AddConversationTurn("hello", "world");
        string exportPath = Path.Combine(_tempDirectory, "session.json");
        ExportCommandHandler sut = new(new ThrowingSelectionPrompt());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session, "json " + exportPath),
            CancellationToken.None);

        result.Message.Should().Contain("Exported session as JSON");
        File.Exists(exportPath).Should().BeTrue();
        ConversationSectionSnapshot? snapshot = await SessionCommandSupport.LoadJsonAsync(
            exportPath,
            CancellationToken.None);
        snapshot.Should().NotBeNull();
        snapshot!.Turns.Should().HaveCount(1);
        snapshot.Turns[0].AssistantResponse.Should().Be("world");
    }

    [Fact]
    public async Task ImportAsync_Should_CreateNewSessionFromJson()
    {
        ReplSessionContext source = CreateSession();
        source.AddConversationTurn("first prompt", "first answer");
        string exportPath = Path.Combine(_tempDirectory, "session.json");
        await SessionCommandSupport.ExportJsonAsync(
            source.CreateSectionSnapshot(DateTimeOffset.UtcNow),
            exportPath,
            CancellationToken.None);
        CapturingSectionStore store = new();
        StoreBackedSessionAppService sessions = new(store);
        ImportCommandHandler sut = new(store, sessions, new ThrowingTextPrompt());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(CreateSession(), exportPath),
            CancellationToken.None);

        result.SessionOverride.Should().NotBeNull();
        result.ReplaySession.Should().BeTrue();
        store.SavedSnapshot.Should().NotBeNull();
        store.SavedSnapshot!.SectionId.Should().NotBe(source.SectionId);
        store.SavedSnapshot.Turns.Should().HaveCount(1);
        result.SessionOverride!.ConversationTurns.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportAsync_Should_RejectNonJsonFiles()
    {
        ImportCommandHandler sut = new(
            new CapturingSectionStore(),
            new ThrowingSessionAppService(),
            new ThrowingTextPrompt());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(CreateSession(), Path.Combine(_tempDirectory, "session.html")),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Contain("JSON");
    }

    [Fact]
    public async Task CloneAsync_Should_SaveCopyAndSwitchToIt()
    {
        ReplSessionContext session = CreateSession();
        session.AddConversationTurn("first prompt", "first answer");
        CapturingSectionStore store = new();
        CloneCommandHandler sut = new(store, new StoreBackedSessionAppService(store));

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session),
            CancellationToken.None);

        result.SessionOverride.Should().NotBeNull();
        result.ReplaySession.Should().BeTrue();
        store.SavedSnapshot.Should().NotBeNull();
        store.SavedSnapshot!.SectionId.Should().NotBe(session.SectionId);
        store.SavedSnapshot.Turns[0].UserInput.Should().Be("first prompt");
    }

    [Fact]
    public async Task CompactAsync_Should_ReplaceOlderTurnsWithSummary()
    {
        ReplSessionContext session = CreateSession();
        for (int index = 1; index <= 5; index++)
        {
            session.AddConversationTurn("prompt " + index, "answer " + index);
        }

        CompactCommandHandler sut = new();

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session, "2"),
            CancellationToken.None);

        result.SessionOverride.Should().BeSameAs(session);
        result.ReplaySession.Should().BeTrue();
        session.ConversationTurns.Should().HaveCount(3);
        session.ConversationTurns[0].UserInput.Should().Be("Compacted previous conversation");
        session.ConversationTurns[0].AssistantResponse.Should().Contain("prompt 1");
        session.ConversationTurns[1].UserInput.Should().Be("prompt 4");
        session.ConversationTurns[2].UserInput.Should().Be("prompt 5");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ReplCommandContext CreateContext(
        ReplSessionContext session,
        string argumentText = "")
    {
        string[] arguments = string.IsNullOrWhiteSpace(argumentText)
            ? []
            : argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string rawText = string.IsNullOrWhiteSpace(argumentText)
            ? "/test"
            : "/test " + argumentText;

        return new ReplCommandContext(
            "test",
            argumentText,
            arguments,
            rawText,
            session);
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a"],
            BuiltInAgentProfiles.Build);
    }

    private sealed class CapturingSectionStore : IConversationSectionStore
    {
        public ConversationSectionSnapshot? SavedSnapshot { get; private set; }

        public Task<ConversationSectionSnapshot?> LoadAsync(
            string sectionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                SavedSnapshot is not null &&
                string.Equals(SavedSnapshot.SectionId, sectionId, StringComparison.OrdinalIgnoreCase)
                    ? SavedSnapshot
                    : null);
        }

        public Task<IReadOnlyList<ConversationSectionSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ConversationSectionSnapshot> snapshots = SavedSnapshot is null
                ? []
                : [SavedSnapshot];
            return Task.FromResult(snapshots);
        }

        public Task SaveAsync(
            ConversationSectionSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            SavedSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class StoreBackedSessionAppService : ISessionAppService
    {
        private readonly CapturingSectionStore _store;

        public StoreBackedSessionAppService(CapturingSectionStore store)
        {
            _store = store;
        }

        public Task<ReplSessionContext> CreateAsync(
            CreateSessionRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void EnsureTitleGenerationStarted(
            ReplSessionContext session,
            string firstUserPrompt)
        {
        }

        public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task<ReplSessionContext> ResumeAsync(
            ResumeSessionRequest request,
            CancellationToken cancellationToken)
        {
            ConversationSectionSnapshot snapshot = await _store.LoadAsync(
                    request.SessionId,
                    cancellationToken)
                ?? throw new InvalidOperationException("Snapshot was not saved.");

            return new ReplSessionContext(
                "NanoAgent",
                snapshot.ProviderProfile,
                snapshot.ActiveModelId,
                snapshot.AvailableModelIds,
                snapshot.SectionId,
                snapshot.Title,
                snapshot.CreatedAtUtc,
                snapshot.UpdatedAtUtc,
                snapshot.TotalEstimatedOutputTokens,
                snapshot.Turns,
                snapshot.PendingExecutionPlan,
                isResumedSection: true,
                agentProfile: BuiltInAgentProfiles.Build,
                reasoningEffort: snapshot.ReasoningEffort,
                sessionState: snapshot.SessionState,
                workspacePath: snapshot.WorkspacePath,
                modelContextWindowTokens: snapshot.ModelContextWindowTokens);
        }

        public Task SaveIfDirtyAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task StopAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingSessionAppService : ISessionAppService
    {
        public Task<ReplSessionContext> CreateAsync(
            CreateSessionRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void EnsureTitleGenerationStarted(
            ReplSessionContext session,
            string firstUserPrompt)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ReplSessionContext> ResumeAsync(
            ResumeSessionRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveIfDirtyAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task StopAsync(
            ReplSessionContext session,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingSelectionPrompt : ISelectionPrompt
    {
        public Task<T> PromptAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            throw new PromptCancelledException();
        }
    }

    private sealed class ThrowingTextPrompt : ITextPrompt
    {
        public Task<string> PromptAsync(
            TextPromptRequest request,
            CancellationToken cancellationToken)
        {
            throw new PromptCancelledException();
        }
    }
}
