using FluentAssertions;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class WorkspacePathTests
{
    [Fact]
    public void IsSamePathOrDescendant_ShouldReturnTrue_ForSamePathAndChildPath()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-workspace-root"));
        string child = Path.Combine(root, "src", "Program.cs");

        WorkspacePath.IsSamePathOrDescendant(root, root).Should().BeTrue();
        WorkspacePath.IsSamePathOrDescendant(root, child).Should().BeTrue();
    }

    [Fact]
    public void IsSamePathOrDescendant_ShouldReturnFalse_ForSiblingPath()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-parent"));
        string sibling = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-parent-2"));

        WorkspacePath.IsSamePathOrDescendant(parent, sibling).Should().BeFalse();
    }

    [Fact]
    public void PathEquals_ShouldNormalizeEquivalentPaths()
    {
        string left = Path.Combine(Path.GetTempPath(), "nanoagent", ".", "src", "..", "src");
        string right = Path.Combine(Path.GetTempPath(), "nanoagent", "src");

        WorkspacePath.PathEquals(left, right).Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldReturnWorkspaceRoot_WhenRequestedPathIsBlank()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-resolve-root"));

        string resolved = WorkspacePath.Resolve(root, "   ");

        resolved.Should().Be(root);
    }

    [Fact]
    public void Resolve_ShouldResolveRelativePathWithinWorkspace()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-resolve"));

        string resolved = WorkspacePath.Resolve(root, Path.Combine("src", "Program.cs"));

        resolved.Should().Be(Path.Combine(root, "src", "Program.cs"));
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenResolvedPathEscapesWorkspace()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-safe-root"));

        Action act = () => WorkspacePath.Resolve(root, Path.Combine("..", "outside.txt"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*within the current workspace*");
    }

    [Fact]
    public void ToRelativePath_ShouldReturnDot_ForWorkspaceRoot()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-relative-root"));

        string relative = WorkspacePath.ToRelativePath(root, root);

        relative.Should().Be(".");
    }

    [Fact]
    public void ToRelativePath_ShouldNormalizeSeparators()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nanoagent-relative"));
        string file = Path.Combine(root, "src", "nested", "Program.cs");

        string relative = WorkspacePath.ToRelativePath(root, file);

        relative.Should().Be("src/nested/Program.cs");
    }
}
