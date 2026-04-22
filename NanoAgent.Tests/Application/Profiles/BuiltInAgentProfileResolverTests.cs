using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Tools;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Profiles;

public sealed class BuiltInAgentProfileResolverTests
{
    [Fact]
    public void Resolve_Should_ReturnBuildProfile_When_NameIsMissing()
    {
        BuiltInAgentProfileResolver sut = new();

        sut.Resolve(null).Name.Should().Be(BuiltInAgentProfiles.BuildName);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("plan")]
    [InlineData("review")]
    public void Resolve_Should_ReturnBuiltInProfile_When_NameMatches(string profileName)
    {
        BuiltInAgentProfileResolver sut = new();

        sut.Resolve(profileName).Name.Should().Be(profileName);
    }

    [Fact]
    public void Resolve_Should_FailClearly_When_ProfileNameIsUnknown()
    {
        BuiltInAgentProfileResolver sut = new();

        Action action = () => sut.Resolve("ops");

        action.Should()
            .Throw<ArgumentException>()
            .WithMessage("*Unknown agent profile 'ops'*build*plan*review*");
    }

    [Fact]
    public void List_Should_ReturnAllBuiltInProfiles()
    {
        BuiltInAgentProfileResolver sut = new();

        sut.List()
            .Select(static profile => profile.Name)
            .Should()
            .Equal("build", "plan", "review");
    }

    [Fact]
    public void PlanAndReviewProfiles_Should_NotEnableWriteTools()
    {
        BuiltInAgentProfileResolver sut = new();

        foreach (string profileName in new[] { "plan", "review" })
        {
            var profile = sut.Resolve(profileName);

            profile.EnabledTools.Should().NotContain(AgentToolNames.ApplyPatch);
            profile.EnabledTools.Should().NotContain(AgentToolNames.FileWrite);
            profile.EnabledTools.Should().Contain(AgentToolNames.ShellCommand);
            profile.PermissionIntent.EditMode.Should().Be(AgentProfileEditMode.ReadOnly);
            profile.PermissionIntent.ShellMode.Should().Be(AgentProfileShellMode.SafeInspectionOnly);
        }
    }

    [Fact]
    public void BuildProfile_Should_DescribeNonInteractiveScaffoldingBehavior()
    {
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("fully specified, non-interactive commands");
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("project name, template or preset, and any confirmation flags");
    }
}
