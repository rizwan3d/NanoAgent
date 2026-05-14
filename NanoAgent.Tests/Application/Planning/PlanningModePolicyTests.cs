using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Planning;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Planning;

public sealed class PlanningModePolicyTests
{
    [Fact]
    public void CreateToolDrivenConversationSystemPrompt_Should_ReturnInstructions_When_BasePromptIsNull()
    {
        string? result = PlanningModePolicy.CreateToolDrivenConversationSystemPrompt(null);

        result.Should().NotBeNull();
        result.Should().Contain("Tool-driven planning");
    }

    [Fact]
    public void CreateToolDrivenConversationSystemPrompt_Should_AppendToBasePrompt()
    {
        string? result = PlanningModePolicy.CreateToolDrivenConversationSystemPrompt("Be helpful.");

        result.Should().StartWith("Be helpful.");
        result.Should().Contain("Tool-driven planning");
    }

    [Fact]
    public void CreateToolDrivenConversationSystemPrompt_Should_ReturnInstructions_When_BasePromptIsWhitespace()
    {
        string? result = PlanningModePolicy.CreateToolDrivenConversationSystemPrompt("   ");

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Tool-driven planning");
    }

    [Fact]
    public void CreateExecutionSystemPrompt_Should_Throw_When_PlanningSummaryIsEmpty()
    {
        Action act = () => PlanningModePolicy.CreateExecutionSystemPrompt("prompt", "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateExecutionSystemPrompt_Should_Throw_When_PlanningSummaryIsNull()
    {
        Action act = () => PlanningModePolicy.CreateExecutionSystemPrompt("prompt", null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateExecutionSystemPrompt_Should_IncludePlanningSummary()
    {
        string? result = PlanningModePolicy.CreateExecutionSystemPrompt(
            "Base prompt",
            "Planning summary here");

        result.Should().Contain("APPROVED EXECUTION PHASE IS ACTIVE");
        result.Should().Contain("Planning summary here");
        result.Should().Contain("Base prompt");
    }

    [Fact]
    public void CreateExecutionSystemPrompt_Should_HandleNullBasePrompt()
    {
        string? result = PlanningModePolicy.CreateExecutionSystemPrompt(
            null,
            "Planning summary");

        result.Should().Contain("APPROVED EXECUTION PHASE IS ACTIVE");
        result.Should().Contain("Planning summary");
    }

    [Theory]
    [InlineData("continue", true)]
    [InlineData("continue with the plan", true)]
    [InlineData("go ahead", true)]
    [InlineData("go ahead with the plan", true)]
    [InlineData("proceed", true)]
    [InlineData("proceed with the plan", true)]
    [InlineData("execute", true)]
    [InlineData("execute the plan", true)]
    [InlineData("implement it", true)]
    [InlineData("implement the plan", true)]
    [InlineData("apply it", true)]
    [InlineData("apply the plan", true)]
    [InlineData("run it", true)]
    [InlineData("run the plan", true)]
    [InlineData("approved", true)]
    [InlineData("approve", true)]
    [InlineData("yes", true)]
    [InlineData("continue and do more", true)]
    [InlineData("no", false)]
    [InlineData("do not proceed", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void IsExecutionApproval_Should_ReturnExpected(string? input, bool expected)
    {
        bool result = PlanningModePolicy.IsExecutionApproval(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("dotnet --version", true)]
    [InlineData("python --version", true)]
    [InlineData("node -v", false)]
    [InlineData("git --version", true)]
    [InlineData("dotnet test", false)]
    [InlineData("rm -rf .", false)]
    [InlineData("", false)]
    [InlineData("somecommand --invalid", false)]
    public void ShouldBypassShellPolicyForPlanningProbe_Should_ReturnExpected(string command, bool expected)
    {
        bool result = PlanningModePolicy.ShouldBypassShellPolicyForPlanningProbe(command);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsExecutionApproval_Should_ReturnFalse_For_EmptyInput()
    {
        PlanningModePolicy.IsExecutionApproval(null).Should().BeFalse();
        PlanningModePolicy.IsExecutionApproval("").Should().BeFalse();
        PlanningModePolicy.IsExecutionApproval("  ").Should().BeFalse();
    }

    [Theory]
    [InlineData("apply_patch")]
    [InlineData("file_write")]
    public void IsWriteLikeTool_Should_ReturnTrue_When_PatchIsNotNull(string _)
    {
        var policy = new ToolPermissionPolicy
        {
            Patch = new PatchPermissionPolicy()
        };

        bool result = PlanningModePolicy.IsWriteLikeTool(policy);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsWriteLikeTool_Should_ReturnTrue_When_HasEditTag()
    {
        var policy = new ToolPermissionPolicy
        {
            ToolTags = ["edit"]
        };

        bool result = PlanningModePolicy.IsWriteLikeTool(policy);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsWriteLikeTool_Should_ReturnTrue_When_HasWriteFilePaths()
    {
        var policy = new ToolPermissionPolicy
        {
            FilePaths =
            [
                new FilePathPermissionRule
                {
                    ArgumentName = "path",
                    Kind = ToolPathAccessKind.Write
                }
            ]
        };

        bool result = PlanningModePolicy.IsWriteLikeTool(policy);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsWriteLikeTool_Should_ReturnFalse_When_OnlyReadPaths()
    {
        var policy = new ToolPermissionPolicy
        {
            FilePaths =
            [
                new FilePathPermissionRule
                {
                    ArgumentName = "path",
                    Kind = ToolPathAccessKind.Read
                }
            ]
        };

        bool result = PlanningModePolicy.IsWriteLikeTool(policy);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsWriteLikeTool_Should_ReturnFalse_For_EmptyPolicy()
    {
        var policy = new ToolPermissionPolicy();

        bool result = PlanningModePolicy.IsWriteLikeTool(policy);

        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateRestrictions_Should_ReturnNull_When_NotPlanningPhase()
    {
        var policy = new ToolPermissionPolicy();
        var context = CreateToolContext(executionPhase: ConversationExecutionPhase.Execution);

        var result = PlanningModePolicy.EvaluateRestrictions(policy, context);

        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateRestrictions_Should_DenyWriteTools_DuringPlanning()
    {
        var policy = new ToolPermissionPolicy
        {
            Patch = new PatchPermissionPolicy()
        };
        var context = CreateToolContext(executionPhase: ConversationExecutionPhase.Planning);

        var result = PlanningModePolicy.EvaluateRestrictions(policy, context);

        result.Should().NotBeNull();
        result!.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("planning_phase_write_blocked");
    }

    [Fact]
    public void EvaluateRestrictions_Should_DenyUnsafeShell_DuringPlanning()
    {
        var policy = new ToolPermissionPolicy
        {
            Shell = new ShellCommandPermissionPolicy
            {
                CommandArgumentName = "command"
            }
        };
        var context = CreateToolContext(
            argumentsJson: """{ "command": "dotnet test" }""",
            executionPhase: ConversationExecutionPhase.Planning);

        var result = PlanningModePolicy.EvaluateRestrictions(policy, context);

        result.Should().NotBeNull();
        result!.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("planning_phase_shell_blocked");
    }

    [Fact]
    public void EvaluateRestrictions_Should_AllowSafeProbeShell_DuringPlanning()
    {
        var policy = new ToolPermissionPolicy
        {
            Shell = new ShellCommandPermissionPolicy
            {
                CommandArgumentName = "command"
            }
        };
        var context = CreateToolContext(
            argumentsJson: """{ "command": "dotnet --version" }""",
            executionPhase: ConversationExecutionPhase.Planning);

        var result = PlanningModePolicy.EvaluateRestrictions(policy, context);

        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateRestrictions_Should_Throw_When_PolicyIsNull()
    {
        Action act = () => PlanningModePolicy.EvaluateRestrictions(null!, CreateToolContext());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvaluateRestrictions_Should_Throw_When_ContextIsNull()
    {
        Action act = () => PlanningModePolicy.EvaluateRestrictions(new ToolPermissionPolicy(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvaluateProfileRestrictions_Should_ReturnNull_When_NotReadOnly()
    {
        var policy = new ToolPermissionPolicy();
        var context = CreateToolContext(agentProfile: BuiltInAgentProfiles.Build);

        var result = PlanningModePolicy.EvaluateProfileRestrictions(policy, context);

        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateProfileRestrictions_Should_DenyWriteTools_When_ReadOnlyProfile()
    {
        var policy = new ToolPermissionPolicy
        {
            Patch = new PatchPermissionPolicy()
        };
        var context = CreateToolContext(agentProfile: BuiltInAgentProfiles.Review);

        var result = PlanningModePolicy.EvaluateProfileRestrictions(policy, context);

        result.Should().NotBeNull();
        result!.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("profile_readonly_write_blocked");
    }

    [Fact]
    public void EvaluateProfileRestrictions_Should_DenyUnsafeShell_When_SafeInspectionProfile()
    {
        var policy = new ToolPermissionPolicy
        {
            Shell = new ShellCommandPermissionPolicy
            {
                CommandArgumentName = "command"
            }
        };
        var context = CreateToolContext(
            argumentsJson: """{ "command": "dotnet test" }""",
            agentProfile: BuiltInAgentProfiles.Plan);

        var result = PlanningModePolicy.EvaluateProfileRestrictions(policy, context);

        result.Should().NotBeNull();
        result!.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("profile_shell_blocked");
    }

    [Fact]
    public void EvaluateProfileRestrictions_Should_AllowSafeShell_When_SafeInspectionProfile()
    {
        var policy = new ToolPermissionPolicy
        {
            Shell = new ShellCommandPermissionPolicy
            {
                CommandArgumentName = "command"
            }
        };
        var context = CreateToolContext(
            argumentsJson: """{ "command": "dotnet --version" }""",
            agentProfile: BuiltInAgentProfiles.Plan);

        var result = PlanningModePolicy.EvaluateProfileRestrictions(policy, context);

        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateProfileRestrictions_Should_Throw_When_PolicyIsNull()
    {
        Action act = () => PlanningModePolicy.EvaluateProfileRestrictions(null!, CreateToolContext());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvaluateProfileRestrictions_Should_Throw_When_ContextIsNull()
    {
        Action act = () => PlanningModePolicy.EvaluateProfileRestrictions(new ToolPermissionPolicy(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSafeInspectionShellCommand_Should_ReturnTrue_For_Probes()
    {
        bool result = PlanningModePolicy.IsSafeInspectionShellCommand("dotnet --version", out _);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSafeInspectionShellCommand_Should_ReturnFalse_For_MutatingCommands()
    {
        bool result = PlanningModePolicy.IsSafeInspectionShellCommand("dotnet test", out string reason);

        result.Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    private static ToolExecutionContext CreateToolContext(
        string argumentsJson = "{}",
        ConversationExecutionPhase executionPhase = ConversationExecutionPhase.Execution,
        IAgentProfile? agentProfile = null)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);

        var session = new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"],
            agentProfile);

        return new ToolExecutionContext(
            "call_1",
            "test_tool",
            document.RootElement.Clone(),
            session,
            executionPhase);
    }
}
