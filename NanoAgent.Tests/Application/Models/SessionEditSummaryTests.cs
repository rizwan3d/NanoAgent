using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class SessionEditSummaryTests
{
    [Fact]
    public void Build_Should_SumPerFile_AttributeRenamesToDestination_AndOrderByMostChanged()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        SessionEditContext[] edits =
        [
            new(now, "file_write (a.cs)", ["a.cs"], 10, 2),
            new(now, "apply_patch (update a.cs)", ["a.cs"], 5, 3),
            new(now, "apply_patch (move old.cs -> new.cs)", ["old.cs -> new.cs"], 1, 1),
        ];

        IReadOnlyList<FileEditSummary> summary = SessionEditSummary.Build(
            edits,
            path => "/abs/" + path);

        summary.Should().HaveCount(2);

        // Most-changed first: a.cs has 15 + 5 = 20 churn, new.cs has 2.
        FileEditSummary first = summary[0];
        first.DisplayPath.Should().Be("a.cs");
        first.AbsolutePath.Should().Be("/abs/a.cs");
        first.AddedLineCount.Should().Be(15);
        first.RemovedLineCount.Should().Be(5);
        first.EditCount.Should().Be(2);

        // Rename is attributed to the destination path, not "old.cs".
        summary[1].DisplayPath.Should().Be("new.cs");
        summary[1].AddedLineCount.Should().Be(1);
    }

    [Fact]
    public void Build_Should_DeriveAction_FromEditDescriptions()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        SessionEditContext[] edits =
        [
            new(now, "file_write created (new.cs)", ["new.cs"], 20, 0),
            new(now, "apply_patch (update existing.cs)", ["existing.cs"], 5, 2),
            new(now, "file_delete (gone.cs)", ["gone.cs"], 0, 9),
            // Created then edited still reads as Created.
            new(now, "apply_patch (add added.cs)", ["added.cs"], 4, 0),
            new(now, "apply_patch (update added.cs)", ["added.cs"], 3, 1),
        ];

        IReadOnlyList<FileEditSummary> summary = SessionEditSummary.Build(edits, path => "/abs/" + path);

        string Action(string path) => summary.Single(f => f.DisplayPath == path).Action;
        Action("new.cs").Should().Be("Created");
        Action("existing.cs").Should().Be("Edited");
        Action("gone.cs").Should().Be("Deleted");
        Action("added.cs").Should().Be("Created");
    }
}
