using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;

namespace NanoAgent.Tests.Application.Profiles;

public sealed class BuiltInAgentProfileTests
{
    [Fact]
    public void Should_Construct_With_ValidArguments()
    {
        var profile = new BuiltInAgentProfile(
            "test",
            AgentProfileMode.Primary,
            "A test profile",
            "You are a test assistant.",
            new HashSet<string>(["tool1", "tool2"]),
            new AgentProfilePermissionOverlay(
                AgentProfileEditMode.AllowEdits,
                AgentProfileShellMode.Default,
                "Test intent"));

        profile.Name.Should().Be("test");
        profile.Mode.Should().Be(AgentProfileMode.Primary);
        profile.Description.Should().Be("A test profile");
        profile.SystemPrompt.Should().Be("You are a test assistant.");
        profile.EnabledTools.Should().BeEquivalentTo(["tool1", "tool2"]);
        profile.PermissionIntent.EditMode.Should().Be(AgentProfileEditMode.AllowEdits);
    }

    [Fact]
    public void Should_Trim_Name_And_Description()
    {
        var profile = new BuiltInAgentProfile(
            "  test  ",
            AgentProfileMode.Subagent,
            "  description  ",
            null,
            new HashSet<string>(["tool"]),
            new AgentProfilePermissionOverlay(
                AgentProfileEditMode.ReadOnly,
                AgentProfileShellMode.SafeInspectionOnly,
                ""));

        profile.Name.Should().Be("test");
        profile.Description.Should().Be("description");
    }

    [Fact]
    public void Should_Set_SystemPrompt_To_Null_When_Whitespace()
    {
        var profile = new BuiltInAgentProfile(
            "test",
            AgentProfileMode.Primary,
            "desc",
            "   ",
            new HashSet<string>(["tool"]),
            new AgentProfilePermissionOverlay(
                AgentProfileEditMode.AllowEdits,
                AgentProfileShellMode.Default,
                ""));

        profile.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public void Should_Throw_When_NameIsEmpty()
    {
        Action act = () => new BuiltInAgentProfile(
            "", AgentProfileMode.Primary, "desc", null,
            new HashSet<string>(), new AgentProfilePermissionOverlay(
                AgentProfileEditMode.AllowEdits, AgentProfileShellMode.Default, ""));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_DescriptionIsEmpty()
    {
        Action act = () => new BuiltInAgentProfile(
            "test", AgentProfileMode.Primary, "", null,
            new HashSet<string>(), new AgentProfilePermissionOverlay(
                AgentProfileEditMode.AllowEdits, AgentProfileShellMode.Default, ""));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_EnabledToolsIsNull()
    {
        Action act = () => new BuiltInAgentProfile(
            "test", AgentProfileMode.Primary, "desc", null,
            null!, new AgentProfilePermissionOverlay(
                AgentProfileEditMode.AllowEdits, AgentProfileShellMode.Default, ""));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Should_Throw_When_PermissionIntentIsNull()
    {
        Action act = () => new BuiltInAgentProfile(
            "test", AgentProfileMode.Primary, "desc", null,
            new HashSet<string>(), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
