using FluentAssertions;
using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Models;

public sealed class PermissionRequestDescriptorTests
{
    [Fact]
    public void Should_Construct_With_ValidArguments()
    {
        var descriptor = new PermissionRequestDescriptor(
            "file_read",
            "read",
            ["tag1", "tag2"],
            ["file1.cs", "file2.cs"]);

        descriptor.ToolName.Should().Be("file_read");
        descriptor.ToolKind.Should().Be("read");
        descriptor.ToolTags.Should().BeEquivalentTo(["tag1", "tag2"]);
        descriptor.Subjects.Should().BeEquivalentTo(["file1.cs", "file2.cs"]);
    }

    [Fact]
    public void Should_Trim_ToolName_And_ToolKind()
    {
        var descriptor = new PermissionRequestDescriptor(
            "  file_read  ",
            "  read  ",
            [],
            []);

        descriptor.ToolName.Should().Be("file_read");
        descriptor.ToolKind.Should().Be("read");
    }

    [Fact]
    public void Should_Filter_And_Deduplicate_Tags()
    {
        var descriptor = new PermissionRequestDescriptor(
            "tool",
            "tool",
            ["tag1", "", "  ", "tag2", "TAG1"],
            []);

        descriptor.ToolTags.Should().BeEquivalentTo(["tag1", "tag2"], opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Should_Filter_And_Deduplicate_Subjects()
    {
        var descriptor = new PermissionRequestDescriptor(
            "tool",
            "tool",
            [],
            ["a.cs", "", "  ", "b.cs", "A.cs"]);

        descriptor.Subjects.Should().BeEquivalentTo(["a.cs", "b.cs"], opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Should_Throw_When_ToolNameIsEmpty()
    {
        Action act = () => new PermissionRequestDescriptor("", "tool", [], []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_ToolKindIsEmpty()
    {
        Action act = () => new PermissionRequestDescriptor("tool", "", [], []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_ToolTagsIsNull()
    {
        Action act = () => new PermissionRequestDescriptor("tool", "tool", null!, []);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Should_Throw_When_SubjectsIsNull()
    {
        Action act = () => new PermissionRequestDescriptor("tool", "tool", [], null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
