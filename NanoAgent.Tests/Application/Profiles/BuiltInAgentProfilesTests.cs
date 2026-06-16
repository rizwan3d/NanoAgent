using FluentAssertions;
using NanoAgent.Application.Profiles;

namespace NanoAgent.Tests.Application.Profiles;

public sealed class BuiltInAgentProfilesTests
{
    [Fact]
    public void All_Should_Contain_Five_Profiles()
    {
        BuiltInAgentProfiles.All.Should().HaveCount(5);
    }

    [Fact]
    public void Primary_Should_Contain_Build_Plan_Review()
    {
        BuiltInAgentProfiles.Primary.Select(p => p.Name)
            .Should().BeEquivalentTo(["build", "plan", "review"]);
    }

    [Fact]
    public void Subagents_Should_Contain_General_Explore()
    {
        BuiltInAgentProfiles.Subagents.Select(p => p.Name)
            .Should().BeEquivalentTo(["general", "explore"]);
    }

    [Fact]
    public void Build_Profile_Should_Have_BuildName()
    {
        BuiltInAgentProfiles.Build.Name.Should().Be("build");
        BuiltInAgentProfiles.Build.Mode.Should().Be(NanoAgent.Application.Models.AgentProfileMode.Primary);
    }

    [Fact]
    public void Plan_Profile_Should_Have_PlanName()
    {
        BuiltInAgentProfiles.Plan.Name.Should().Be("plan");
        BuiltInAgentProfiles.Plan.Mode.Should().Be(NanoAgent.Application.Models.AgentProfileMode.Primary);
    }

    [Fact]
    public void Review_Profile_Should_Have_ReviewName()
    {
        BuiltInAgentProfiles.Review.Name.Should().Be("review");
        BuiltInAgentProfiles.Review.Mode.Should().Be(NanoAgent.Application.Models.AgentProfileMode.Primary);
    }

    [Fact]
    public void General_Profile_Should_Have_GeneralName()
    {
        BuiltInAgentProfiles.General.Name.Should().Be("general");
        BuiltInAgentProfiles.General.Mode.Should().Be(NanoAgent.Application.Models.AgentProfileMode.Subagent);
    }

    [Fact]
    public void Explore_Profile_Should_Have_ExploreName()
    {
        BuiltInAgentProfiles.Explore.Name.Should().Be("explore");
        BuiltInAgentProfiles.Explore.Mode.Should().Be(NanoAgent.Application.Models.AgentProfileMode.Subagent);
    }

    [Theory]
    [InlineData("build", "build")]
    [InlineData("BUILD", "build")]
    [InlineData("Plan", "plan")]
    [InlineData("REVIEW", "review")]
    [InlineData("general", "general")]
    [InlineData("Explore", "explore")]
    [InlineData(null, "build")]
    [InlineData("", "build")]
    [InlineData("  ", "build")]
    public void Resolve_Should_ReturnCorrectProfile(string? name, string expectedName)
    {
        var profile = BuiltInAgentProfiles.Resolve(name);

        profile.Name.Should().Be(expectedName);
    }

    [Fact]
    public void Resolve_Should_Throw_For_UnknownProfileName()
    {
        Action act = () => BuiltInAgentProfiles.Resolve("unknown");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown agent profile*");
    }

    [Fact]
    public void BuildProfile_Should_Have_ShellCommand_Enabled()
    {
        BuiltInAgentProfiles.Build.EnabledTools.Should().Contain("shell_command");
    }

    [Fact]
    public void ExploreProfile_Should_Not_Have_ApplyPatch()
    {
        BuiltInAgentProfiles.Explore.EnabledTools.Should().NotContain("apply_patch");
    }

    [Fact]
    public void PlanProfile_Should_Have_AskQuestion_Enabled()
    {
        BuiltInAgentProfiles.Plan.EnabledTools.Should().Contain("ask_question");
    }

    [Fact]
    public void BuildProfile_Should_Have_AskQuestion_Enabled()
    {
        BuiltInAgentProfiles.Build.EnabledTools.Should().Contain("ask_question");
    }
}
