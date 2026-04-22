using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Profiles;
using NanoAgent.Domain.Models;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Permissions;

public sealed class ToolPermissionEvaluatorTests : IDisposable
{
    private readonly string _workspaceRoot;

    public ToolPermissionEvaluatorTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Permissions-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs"));
    }

    [Fact]
    public void Evaluate_Should_Allow_When_PathIsWithinAllowedRoot()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Read,
                        AllowedRoots = ["src"]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext("""{ "path": "src/app.cs" }""")));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_Deny_When_PathFallsOutsideAllowedRoot()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Write,
                        AllowedRoots = ["src"]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext("""{ "path": "docs/readme.md" }""")));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("path_not_allowed");
    }

    [Fact]
    public void Evaluate_Should_ReturnRequiresApproval_When_PolicyRequiresApproval()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ApprovalMode = ToolApprovalMode.RequireApproval
            },
            new PermissionEvaluationContext(CreateContext("{}")));

        result.Decision.Should().Be(PermissionEvaluationDecision.RequiresApproval);
        result.ReasonCode.Should().Be("permission_approval_required");
    }

    [Fact]
    public void Evaluate_Should_Deny_When_ShellCommandIsNotAllowlisted()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["git", "dotnet"]
                }
            },
            new PermissionEvaluationContext(CreateContext("""{ "command": "rm -rf ." }""")));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("shell_command_not_allowed");
    }

    [Fact]
    public void Evaluate_Should_AllowToolchainCommands_When_CommandIsAllowlisted()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["dotnet", "npm", "python"]
                }
            },
            new PermissionEvaluationContext(CreateContext("""{ "command": "npm test" }""")));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_DenyWriteTools_When_ProfileIsReadOnly()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ToolTags = ["edit"],
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Write,
                        AllowedRoots = ["."]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "path": "src/app.cs" }""",
                CreateSession(BuiltInAgentProfiles.Review))));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("profile_readonly_write_blocked");
    }

    [Fact]
    public void Evaluate_Should_DenyMutatingShellCommands_When_ProfileAllowsSafeInspectionOnly()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["dotnet", "npm", "git"]
                }
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "command": "dotnet test" }""",
                CreateSession(BuiltInAgentProfiles.Plan))));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("profile_shell_blocked");
    }

    [Fact]
    public void Evaluate_Should_Deny_When_ReadRuleMatchesDotEnvPattern()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor(new PermissionSettings
            {
                DefaultMode = PermissionMode.Ask,
                Rules =
                [
                    new PermissionRule
                    {
                        Tools = ["read"],
                        Mode = PermissionMode.Allow
                    },
                    new PermissionRule
                    {
                        Tools = ["read"],
                        Mode = PermissionMode.Deny,
                        Patterns = [".env"]
                    }
                ]
            }));

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ToolTags = ["read"],
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Read,
                        AllowedRoots = ["."]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext("""{ "path": ".env" }""")));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("permission_policy_denied");
    }

    [Fact]
    public void Evaluate_Should_Allow_When_AgentOverrideMatchesDeniedReadPattern()
    {
        ReplSessionContext session = CreateSession();
        session.AddPermissionOverride(new PermissionRule
        {
            Tools = ["read"],
            Mode = PermissionMode.Allow,
            Patterns = [".env"]
        });

        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor(new PermissionSettings
            {
                DefaultMode = PermissionMode.Ask,
                Rules =
                [
                    new PermissionRule
                    {
                        Tools = ["read"],
                        Mode = PermissionMode.Allow
                    },
                    new PermissionRule
                    {
                        Tools = ["read"],
                        Mode = PermissionMode.Deny,
                        Patterns = [".env"]
                    }
                ]
            }));

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ToolTags = ["read"],
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Read,
                        AllowedRoots = ["."]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext("""{ "path": ".env" }""", session)));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_DenyWriteTools_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ToolTags = ["edit"],
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Write,
                        AllowedRoots = ["src"]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "path": "src/app.cs" }""",
                executionPhase: ConversationExecutionPhase.Planning)));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("planning_phase_write_blocked");
    }

    [Fact]
    public void Evaluate_Should_AllowReadOnlyTools_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ToolTags = ["read"],
                FilePaths =
                [
                    new FilePathPermissionRule
                    {
                        ArgumentName = "path",
                        Kind = ToolPathAccessKind.Read,
                        AllowedRoots = ["src"]
                    }
                ]
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "path": "src/app.cs" }""",
                executionPhase: ConversationExecutionPhase.Planning)));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_DenyUnsafeShellCommands_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["git", "rg"]
                }
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "command": "git checkout main" }""",
                executionPhase: ConversationExecutionPhase.Planning)));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("planning_phase_shell_blocked");
    }

    [Fact]
    public void Evaluate_Should_AllowSafeToolchainProbeShellCommands_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["git", "rg"]
                }
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "command": "python --version" }""",
                executionPhase: ConversationExecutionPhase.Planning)));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_AllowToolLookupShellCommands_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            new StubPermissionConfigurationAccessor());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["git", "rg"]
                }
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "command": "where.exe dotnet" }""",
                executionPhase: ConversationExecutionPhase.Planning)));

        result.IsAllowed.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private static ToolExecutionContext CreateContext(
        string argumentsJson,
        ReplSessionContext? session = null,
        ConversationExecutionPhase executionPhase = ConversationExecutionPhase.Execution)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);

        return new ToolExecutionContext(
            "call_1",
            "tool",
            document.RootElement.Clone(),
            session ?? CreateSession(),
            executionPhase);
    }

    private static ReplSessionContext CreateSession(IAgentProfile? agentProfile = null)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"],
            agentProfile);
    }

    private sealed class StubWorkspaceRootProvider : global::NanoAgent.Application.Abstractions.IWorkspaceRootProvider
    {
        private readonly string _workspaceRoot;

        public StubWorkspaceRootProvider(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public string GetWorkspaceRoot()
        {
            return _workspaceRoot;
        }
    }

    private sealed class StubPermissionConfigurationAccessor : global::NanoAgent.Application.Abstractions.IPermissionConfigurationAccessor
    {
        private readonly PermissionSettings _settings;

        public StubPermissionConfigurationAccessor(PermissionSettings? settings = null)
        {
            _settings = settings ?? new PermissionSettings
            {
                DefaultMode = PermissionMode.Ask,
                Rules = []
            };
        }

        public PermissionSettings GetSettings()
        {
            return _settings;
        }
    }
}
