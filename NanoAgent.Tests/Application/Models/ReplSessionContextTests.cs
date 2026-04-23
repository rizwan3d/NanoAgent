using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Models;

public sealed class ReplSessionContextTests
{
    [Fact]
    public void RecordFileEditTransaction_Should_ExposePendingUndo_And_CompleteUndoShouldMoveItToRedo()
    {
        ReplSessionContext session = CreateSession();
        WorkspaceFileEditTransaction transaction = CreateTransaction("file_write (README.md)");

        session.RecordFileEditTransaction(transaction);

        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? pendingUndo).Should().BeTrue();
        pendingUndo.Should().BeSameAs(transaction);

        session.CompleteUndoFileEdit();

        session.TryGetPendingUndoFileEdit(out _).Should().BeFalse();
        session.TryGetPendingRedoFileEdit(out WorkspaceFileEditTransaction? pendingRedo).Should().BeTrue();
        pendingRedo.Should().BeSameAs(transaction);
    }

    [Fact]
    public void CompleteRedoFileEdit_Should_MoveTransactionBackToUndoStack()
    {
        ReplSessionContext session = CreateSession();
        WorkspaceFileEditTransaction transaction = CreateTransaction("apply_patch (2 files)");
        session.RecordFileEditTransaction(transaction);
        session.CompleteUndoFileEdit();

        session.CompleteRedoFileEdit();

        session.TryGetPendingRedoFileEdit(out _).Should().BeFalse();
        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? pendingUndo).Should().BeTrue();
        pendingUndo.Should().BeSameAs(transaction);
    }

    [Fact]
    public void RecordFileEditTransaction_Should_ClearRedoStack_When_NewEditArrivesAfterUndo()
    {
        ReplSessionContext session = CreateSession();
        session.RecordFileEditTransaction(CreateTransaction("file_write (README.md)"));
        session.CompleteUndoFileEdit();

        session.RecordFileEditTransaction(CreateTransaction("apply_patch (2 files)"));

        session.TryGetPendingRedoFileEdit(out _).Should().BeFalse();
    }

    [Fact]
    public void BeginFileEditTransactionBatch_Should_GroupMultipleEditsIntoOneUndoEntry()
    {
        ReplSessionContext session = CreateSession();

        using (session.BeginFileEditTransactionBatch())
        {
            session.RecordFileEditTransaction(new WorkspaceFileEditTransaction(
                "file_write (README.md)",
                [new WorkspaceFileEditState("README.md", exists: false, content: null)],
                [new WorkspaceFileEditState("README.md", exists: true, content: "hello")]));
            session.RecordFileEditTransaction(new WorkspaceFileEditTransaction(
                "file_write (src/App.js)",
                [new WorkspaceFileEditState("src/App.js", exists: true, content: "old")],
                [new WorkspaceFileEditState("src/App.js", exists: true, content: "new")]));
        }

        session.TryGetPendingUndoFileEdit(out WorkspaceFileEditTransaction? pendingUndo).Should().BeTrue();
        pendingUndo!.Description.Should().Be("tool round (2 edits across 2 files)");
        pendingUndo.BeforeStates.Should().HaveCount(2);
        pendingUndo.AfterStates.Should().HaveCount(2);
        pendingUndo.BeforeStates.Select(static state => state.Path).Should().Equal("README.md", "src/App.js");
        pendingUndo.AfterStates.Select(static state => state.Path).Should().Equal("README.md", "src/App.js");
    }

    [Fact]
    public void SetPendingExecutionPlan_Should_ExposePendingPlan_And_ClearShouldRemoveIt()
    {
        ReplSessionContext session = CreateSession();
        PendingExecutionPlan pendingPlan = new(
            "plan the refactor",
            "Plan\n1. Inspect\n2. Edit\n3. Validate",
            ["Inspect", "Edit", "Validate"]);

        session.SetPendingExecutionPlan(pendingPlan);

        session.HasPendingExecutionPlan.Should().BeTrue();
        session.PendingExecutionPlan.Should().NotBeNull();
        session.PendingExecutionPlan!.PlanningSummary.Should().Contain("Plan");
        session.PendingExecutionPlan.Tasks.Should().Equal("Inspect", "Edit", "Validate");

        session.ClearPendingExecutionPlan();

        session.HasPendingExecutionPlan.Should().BeFalse();
        session.PendingExecutionPlan.Should().BeNull();
    }

    [Fact]
    public void SetAgentProfile_Should_UpdateProfile_AndPersistUpdatedProfileName()
    {
        ReplSessionContext session = CreateSession();

        session.SetAgentProfile(BuiltInAgentProfiles.Plan);
        ConversationSectionSnapshot snapshot = session.CreateSectionSnapshot(DateTimeOffset.UtcNow.AddMinutes(1));

        session.AgentProfile.Name.Should().Be(BuiltInAgentProfiles.PlanName);
        session.IsPersistedStateDirty.Should().BeTrue();
        snapshot.AgentProfileName.Should().Be(BuiltInAgentProfiles.PlanName);
    }

    [Fact]
    public void SetReasoningEffort_Should_UpdateThinkingEffort_AndPersistItInSnapshot()
    {
        ReplSessionContext session = CreateSession();

        bool changed = session.SetReasoningEffort("HIGH");
        ConversationSectionSnapshot snapshot = session.CreateSectionSnapshot(DateTimeOffset.UtcNow.AddMinutes(1));

        changed.Should().BeTrue();
        session.ReasoningEffort.Should().Be("high");
        session.IsPersistedStateDirty.Should().BeTrue();
        snapshot.ReasoningEffort.Should().Be("high");
    }

    [Fact]
    public void ClearReasoningEffort_Should_ResetThinkingEffortToProviderDefault()
    {
        ReplSessionContext session = CreateSession();
        session.SetReasoningEffort("medium");

        bool changed = session.ClearReasoningEffort();

        changed.Should().BeTrue();
        session.ReasoningEffort.Should().BeNull();
    }

    [Fact]
    public void SessionState_Should_PersistFileEditAndTerminalContext_InSnapshot()
    {
        ReplSessionContext session = CreateSession();
        DateTimeOffset observedAtUtc = new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        session.RecordFileContext(new SessionFileContext(
            "README.md",
            "read",
            observedAtUtc,
            "Read 100 characters. Excerpt: NanoAgent helps with coding tasks."));
        session.RecordEditContext(new SessionEditContext(
            observedAtUtc.AddMinutes(1),
            "file_write (README.md)",
            ["README.md"],
            2,
            1));
        session.RecordTerminalCommand(new SessionTerminalCommand(
            observedAtUtc.AddMinutes(2),
            "dotnet test NanoAgent.slnx",
            ".",
            0,
            "Passed! Total: 292",
            null));

        ConversationSectionSnapshot snapshot = session.CreateSectionSnapshot(session.SectionCreatedAtUtc.AddMinutes(3));
        ReplSessionContext resumedSession = new(
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
            agentProfile: BuiltInAgentProfiles.Resolve(snapshot.AgentProfileName),
            reasoningEffort: snapshot.ReasoningEffort,
            sessionState: snapshot.SessionState);

        snapshot.SessionState.Files.Should().ContainSingle();
        snapshot.SessionState.Edits.Should().ContainSingle();
        snapshot.SessionState.TerminalHistory.Should().ContainSingle();
        resumedSession.CreateStatefulContextPrompt().Should().Contain("README.md");
        resumedSession.CreateStatefulContextPrompt().Should().Contain("file_write (README.md)");
        resumedSession.CreateStatefulContextPrompt().Should().Contain("dotnet test NanoAgent.slnx");
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini"]);
    }

    private static WorkspaceFileEditTransaction CreateTransaction(string description)
    {
        return new WorkspaceFileEditTransaction(
            description,
            [new WorkspaceFileEditState("README.md", exists: false, content: null)],
            [new WorkspaceFileEditState("README.md", exists: true, content: "hello")]);
    }
}
