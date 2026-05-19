using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class SessionCommandSupportTests : IDisposable
{
    private readonly string _tempRoot;

    public SessionCommandSupportTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "nanoagent-session-support-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void CreateDefaultExportPath_ShouldSanitizeTitleAndExtension()
    {
        ReplSessionContext session = CreateSession(sectionTitle: " My Session: v1/2? ");
        string currentDirectory = Directory.GetCurrentDirectory();

        string path = SessionCommandSupport.CreateDefaultExportPath(session, ".json");

        Path.GetDirectoryName(path).Should().Be(currentDirectory);
        Path.GetFileName(path).Should().Be($"nanoagent-my-session-v12-{session.SectionId[..8]}.json");
    }

    [Fact]
    public void ResolvePath_ShouldExpandQuotedHomeRelativePath()
    {
        string resolved = SessionCommandSupport.ResolvePath("\"~/exports/session.json\"");
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "exports",
            "session.json");

        resolved.Should().Be(Path.GetFullPath(expected));
    }

    [Fact]
    public async Task LoadJsonAsync_ShouldReturnNull_WhenJsonIsInvalid()
    {
        string path = Path.Combine(_tempRoot, "broken.json");
        await File.WriteAllTextAsync(path, "{ not valid json");

        ConversationSectionSnapshot? snapshot = await SessionCommandSupport.LoadJsonAsync(path, CancellationToken.None);

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task ExportHtmlAsync_ShouldWriteEscapedTranscript()
    {
        ConversationSectionSnapshot snapshot = CreateSnapshot(
            title: "Session <One>",
            turns:
            [
                new ConversationSectionTurn(
                    "user <prompt>",
                    "assistant & response",
                    [new ConversationToolCall("call-1", "file_write", """{ "path": "README.md" }""")],
                    ["<tool output>"])
            ]);
        string path = Path.Combine(_tempRoot, "nested", "session.html");

        await SessionCommandSupport.ExportHtmlAsync(snapshot, path, CancellationToken.None);

        string html = await File.ReadAllTextAsync(path);
        html.Should().Contain("<!doctype html>");
        html.Should().Contain("Session &lt;One&gt;");
        html.Should().Contain("user &lt;prompt&gt;");
        html.Should().Contain("assistant &amp; response");
        html.Should().Contain("&lt;tool output&gt;");
        html.Should().Contain("Tool: file_write");
    }

    [Fact]
    public void CreateImportedSnapshot_ShouldAppendImportedSuffixOnlyOnce_AndUseCurrentWorkspace()
    {
        ConversationSectionSnapshot imported = CreateSnapshot(title: "Release Notes");
        ReplSessionContext currentSession = CreateSession(workspacePath: Path.Combine(_tempRoot, "workspace-current"));

        ConversationSectionSnapshot result = SessionCommandSupport.CreateImportedSnapshot(imported, currentSession);

        result.SectionId.Should().NotBe(imported.SectionId);
        result.Title.Should().Be("Release Notes imported");
        result.WorkspacePath.Should().Be(Path.GetFullPath(currentSession.WorkspacePath));
        result.ProviderProfile.Should().Be(imported.ProviderProfile);
        result.Turns.Should().BeEquivalentTo(imported.Turns);

        ConversationSectionSnapshot alreadyImported = CreateSnapshot(title: "Release Notes imported");
        SessionCommandSupport.CreateImportedSnapshot(alreadyImported, currentSession)
            .Title.Should().Be("Release Notes imported");
    }

    [Fact]
    public void TryNormalizeSessionId_ShouldNormalizeValidGuid_AndRejectInvalid()
    {
        string input = Guid.NewGuid().ToString("B").ToUpperInvariant();

        bool success = SessionCommandSupport.TryNormalizeSessionId(input, out string normalized);
        bool invalid = SessionCommandSupport.TryNormalizeSessionId("not-a-guid", out string failedValue);

        success.Should().BeTrue();
        normalized.Should().MatchRegex("^[0-9a-f\\-]{36}$");
        invalid.Should().BeFalse();
        failedValue.Should().BeEmpty();
    }

    [Fact]
    public void CreateTitleWithSuffix_AndCreatePreview_ShouldNormalizeOutput()
    {
        SessionCommandSupport.CreateTitleWithSuffix("  ", "copy")
            .Should().Be(ReplSessionContext.DefaultSectionTitle + " copy");
        SessionCommandSupport.CreateTitleWithSuffix("Build Plan copy", "copy")
            .Should().Be("Build Plan copy");

        string preview = SessionCommandSupport.CreatePreview("  line one \n\n line   two\tthree  ", maxLength: 14);

        preview.Should().Be("line one li...");
    }

    [Fact]
    public void CreateCopySnapshot_ShouldRespectIncludeStateAndClampNegativeTokens()
    {
        PendingExecutionPlan plan = new("build app", "summary", ["inspect", "test"]);
        SessionStateSnapshot state = new(
            [new SessionFileContext("README.md", "read", new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero), "Read file.")],
            [new SessionEditContext(new DateTimeOffset(2026, 5, 20, 0, 1, 0, TimeSpan.Zero), "edit", ["README.md"], 1, 0)],
            [new SessionTerminalCommand(new DateTimeOffset(2026, 5, 20, 0, 2, 0, TimeSpan.Zero), "dotnet test", ".", 0, "Passed", null)]);
        ReplSessionContext source = CreateSession(
            sectionTitle: "Source Section",
            sessionState: state,
            pendingExecutionPlan: plan);
        ConversationSectionTurn turn = new("prompt", "response");

        ConversationSectionSnapshot withoutState = SessionCommandSupport.CreateCopySnapshot(
            source,
            "Copied",
            [turn],
            totalEstimatedOutputTokens: -5,
            includeState: false);
        ConversationSectionSnapshot withState = SessionCommandSupport.CreateCopySnapshot(
            source,
            "Copied",
            [turn],
            totalEstimatedOutputTokens: 9,
            includeState: true);

        withoutState.TotalEstimatedOutputTokens.Should().Be(0);
        withoutState.PendingExecutionPlan.Should().BeNull();
        withoutState.SessionState.Should().Be(SessionStateSnapshot.Empty);
        withState.TotalEstimatedOutputTokens.Should().Be(9);
        withState.PendingExecutionPlan.Should().Be(plan);
        withState.SessionState.Should().BeEquivalentTo(state);
        withState.AgentProfileName.Should().Be(source.AgentProfile.Name);
    }

    [Fact]
    public async Task SaveAndResumeAsync_ShouldSaveSnapshotAndResumeUsingSavedSectionId()
    {
        ConversationSectionSnapshot snapshot = CreateSnapshot();
        CapturingSectionStore store = new();
        ResumeStubSessionAppService sessionAppService = new();

        ReplSessionContext resumed = await SessionCommandSupport.SaveAndResumeAsync(
            snapshot,
            store,
            sessionAppService,
            CancellationToken.None);

        store.SavedSnapshot.Should().Be(snapshot);
        sessionAppService.LastRequest.Should().NotBeNull();
        sessionAppService.LastRequest!.SessionId.Should().Be(snapshot.SectionId);
        resumed.SectionId.Should().Be(snapshot.SectionId);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ReplSessionContext CreateSession(
        string sectionTitle = "Build Session",
        string? workspacePath = null,
        SessionStateSnapshot? sessionState = null,
        PendingExecutionPlan? pendingExecutionPlan = null)
    {
        return new ReplSessionContext(
            "NanoAgent",
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://api.example.test/v1"),
            "model-a",
            ["model-a", "model-b"],
            sectionTitle: sectionTitle,
            agentProfile: BuiltInAgentProfiles.Build,
            sessionState: sessionState,
            workspacePath: workspacePath ?? Path.Combine(_tempRoot, "workspace"),
            pendingExecutionPlan: pendingExecutionPlan,
            modelContextWindowTokens: new Dictionary<string, int>
            {
                ["model-a"] = 128000
            },
            activeProviderName: "Example Provider");
    }

    private ConversationSectionSnapshot CreateSnapshot(
        string title = "Build Snapshot",
        IReadOnlyList<ConversationSectionTurn>? turns = null)
    {
        DateTimeOffset createdAt = new(2026, 5, 20, 1, 0, 0, TimeSpan.Zero);
        return new ConversationSectionSnapshot(
            Guid.NewGuid().ToString("D"),
            title,
            createdAt,
            createdAt.AddMinutes(5),
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://api.example.test/v1"),
            "model-a",
            ["model-a", "model-b"],
            turns ?? [new ConversationSectionTurn("hello", "world")],
            12,
            agentProfileName: BuiltInAgentProfiles.BuildName,
            reasoningEffort: "medium",
            sessionState: SessionStateSnapshot.Empty,
            workspacePath: Path.Combine(_tempRoot, "snapshot-workspace"),
            modelContextWindowTokens: new Dictionary<string, int>
            {
                ["model-a"] = 128000
            },
            activeProviderName: "Example Provider");
    }

    private sealed class CapturingSectionStore : IConversationSectionStore
    {
        public ConversationSectionSnapshot? SavedSnapshot { get; private set; }

        public Task<ConversationSectionSnapshot?> LoadAsync(string sectionId, CancellationToken cancellationToken)
            => Task.FromResult(
                SavedSnapshot is not null &&
                string.Equals(SavedSnapshot.SectionId, sectionId, StringComparison.Ordinal)
                    ? SavedSnapshot
                    : null);

        public Task<IReadOnlyList<ConversationSectionSnapshot>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ConversationSectionSnapshot>>(SavedSnapshot is null ? [] : [SavedSnapshot]);

        public Task SaveAsync(ConversationSectionSnapshot snapshot, CancellationToken cancellationToken)
        {
            SavedSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class ResumeStubSessionAppService : ISessionAppService
    {
        public ResumeSessionRequest? LastRequest { get; private set; }

        public Task<ReplSessionContext> CreateAsync(CreateSessionRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ReplSessionContext> CreateNewSectionInSessionAsync(
            ReplSessionContext currentSession,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void EnsureTitleGenerationStarted(ReplSessionContext session, string firstUserPrompt)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ReplSessionContext> ResumeAsync(ResumeSessionRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ReplSessionContext(
                "NanoAgent",
                new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://api.example.test/v1"),
                "model-a",
                ["model-a"],
                sectionId: request.SessionId,
                agentProfile: BuiltInAgentProfiles.Build,
                workspacePath: Path.Combine(Path.GetTempPath(), "resumed-workspace")));
        }

        public Task SaveIfDirtyAsync(ReplSessionContext session, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task StopAsync(ReplSessionContext session, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
