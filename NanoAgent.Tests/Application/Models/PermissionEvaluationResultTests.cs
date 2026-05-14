using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class PermissionEvaluationResultTests
{
    [Fact]
    public void Allowed_Should_Create_AllowedResult()
    {
        var result = PermissionEvaluationResult.Allowed();

        result.Decision.Should().Be(PermissionEvaluationDecision.Allowed);
        result.IsAllowed.Should().BeTrue();
        result.ReasonCode.Should().BeNull();
        result.Reason.Should().BeNull();
        result.Request.Should().BeNull();
        result.EffectiveMode.Should().BeNull();
    }

    [Fact]
    public void Allowed_Should_Set_EffectiveMode_And_Request()
    {
        var request = new PermissionRequestDescriptor("tool", "tool", ["tag"], []);
        var result = PermissionEvaluationResult.Allowed(PermissionMode.Allow, request);

        result.EffectiveMode.Should().Be(PermissionMode.Allow);
        result.Request.Should().Be(request);
    }

    [Fact]
    public void Denied_Should_Create_DeniedResult()
    {
        var result = PermissionEvaluationResult.Denied("reason_code", "Access denied");

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.IsAllowed.Should().BeFalse();
        result.ReasonCode.Should().Be("reason_code");
        result.Reason.Should().Be("Access denied");
    }

    [Fact]
    public void Denied_Should_Throw_When_ReasonCodeIsEmpty()
    {
        Action act = () => PermissionEvaluationResult.Denied("", "reason");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Denied_Should_Throw_When_ReasonIsEmpty()
    {
        Action act = () => PermissionEvaluationResult.Denied("code", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequiresApproval_Should_Create_RequiresApprovalResult()
    {
        var result = PermissionEvaluationResult.RequiresApproval("approval_needed", "Need approval");

        result.Decision.Should().Be(PermissionEvaluationDecision.RequiresApproval);
        result.IsAllowed.Should().BeFalse();
        result.ReasonCode.Should().Be("approval_needed");
        result.Reason.Should().Be("Need approval");
    }

    [Fact]
    public void RequiresApproval_Should_Throw_When_ReasonCodeIsEmpty()
    {
        Action act = () => PermissionEvaluationResult.RequiresApproval("", "reason");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Denied_Should_Trim_ReasonCode_And_Reason()
    {
        var result = PermissionEvaluationResult.Denied("  code  ", "  reason  ");

        result.ReasonCode.Should().Be("code");
        result.Reason.Should().Be("reason");
    }
}
