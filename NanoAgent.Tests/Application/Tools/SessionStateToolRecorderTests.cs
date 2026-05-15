using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Tests.Application.Tools;

public sealed class SessionStateToolRecorderTests
{
    [Fact]
    public void RecordApplyPatch_Should_PreserveLeadingWhitespace_InPreviewSummary()
    {
        ReplSessionContext session = TestSessionFactory.Create();

        SessionStateToolRecorder.RecordApplyPatch(
            session,
            new WorkspaceApplyPatchResult(
                1,
                1,
                1,
                [
                    new WorkspaceApplyPatchFileResult(
                        "script.py",
                        "update",
                        null,
                        1,
                        1,
                        [
                            new WorkspaceFileWritePreviewLine(4, "context", "def greet():"),
                            new WorkspaceFileWritePreviewLine(5, "remove", "    print(\"Hi\")"),
                            new WorkspaceFileWritePreviewLine(5, "add", "    print(\"Hello\")")
                        ],
                        0)
                ]));

        ConversationSectionSnapshot snapshot = session.CreateSectionSnapshot(
            session.SectionCreatedAtUtc.AddMinutes(1));

        snapshot.SessionState.Files.Should().ContainSingle();
        snapshot.SessionState.Files[0].Summary.Should().Contain("remove@5:     print(\"Hi\")");
        snapshot.SessionState.Files[0].Summary.Should().Contain("add@5:     print(\"Hello\")");
    }
}
