using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class AgentProfilePermissionOverlayTests
{
    [Fact]
    public void Should_Construct_With_ReadOnlyMode()
    {
        var overlay = new AgentProfilePermissionOverlay(
            AgentProfileEditMode.ReadOnly,
            AgentProfileShellMode.SafeInspectionOnly,
            "Read-only inspection");

        overlay.EditMode.Should().Be(AgentProfileEditMode.ReadOnly);
        overlay.ShellMode.Should().Be(AgentProfileShellMode.SafeInspectionOnly);
        overlay.BehaviorIntent.Should().Be("Read-only inspection");
    }

    [Fact]
    public void Should_Construct_With_AllowEditsMode()
    {
        var overlay = new AgentProfilePermissionOverlay(
            AgentProfileEditMode.AllowEdits,
            AgentProfileShellMode.Default,
            "Normal behavior");

        overlay.EditMode.Should().Be(AgentProfileEditMode.AllowEdits);
        overlay.ShellMode.Should().Be(AgentProfileShellMode.Default);
    }
}
