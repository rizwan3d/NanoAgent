using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Tools;
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
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
    public void Evaluate_Should_AllowChainedCommands_When_AllSegmentsAreAllowlisted()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["node", "npm"]
                }
            },
            new PermissionEvaluationContext(CreateContext("""{ "command": "node -v && npm -v" }""")));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_DenyChainedCommands_When_AnySegmentIsNotAllowlisted()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                Shell = new ShellCommandPermissionPolicy
                {
                    CommandArgumentName = "command",
                    AllowedCommands = ["node", "npm"]
                }
            },
            new PermissionEvaluationContext(CreateContext("""{ "command": "node -v && rm -rf ." }""")));

        result.Decision.Should().Be(PermissionEvaluationDecision.Denied);
        result.ReasonCode.Should().Be("shell_command_not_allowed");
    }

    [Fact]
    public void Evaluate_Should_DenyWriteTools_When_ProfileIsReadOnly()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
            CreatePermissionSettings(new PermissionSettings
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
            CreatePermissionSettings(new PermissionSettings
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
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
    public void Evaluate_Should_BypassUserPermissionRules_When_PolicyRequestsIt()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings(new PermissionSettings
            {
                DefaultMode = PermissionMode.Ask,
                Rules =
                [
                    new PermissionRule
                    {
                        Tools = ["planning_mode"],
                        Mode = PermissionMode.Deny
                    }
                ]
            }));

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ApprovalMode = ToolApprovalMode.RequireApproval,
                BypassUserPermissionRules = true
            },
            new PermissionEvaluationContext(CreateContext("{}", toolName: "planning_mode")));

        result.IsAllowed.Should().BeTrue();
        result.EffectiveMode.Should().Be(PermissionMode.Allow);
    }

    [Fact]
    public void Evaluate_Should_DenyUnsafeShellCommands_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings());

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
            CreatePermissionSettings());

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
    public void Evaluate_Should_CollectNestedWebRunSubjects_ForPermissionMatching()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings());

        PermissionEvaluationResult result = sut.Evaluate(
            new ToolPermissionPolicy
            {
                ApprovalMode = ToolApprovalMode.RequireApproval,
                WebRequest = new WebRequestPermissionPolicy
                {
                    RequestArgumentName = "search_query"
                }
            },
            new PermissionEvaluationContext(CreateContext(
                """{ "search_query": [{ "q": "dotnet docs" }], "open": [{ "ref_id": "https://example.com" }] }""",
                toolName: AgentToolNames.WebRun)));

        result.Decision.Should().Be(PermissionEvaluationDecision.RequiresApproval);
        result.Request.Should().NotBeNull();
        result.Request!.Subjects.Should().Contain("dotnet docs");
    }

    [Fact]
    public void Evaluate_Should_AllowToolLookupShellCommands_When_PlanningModeIsActive()
    {
        ToolPermissionEvaluator sut = new(
            new StubWorkspaceRootProvider(_workspaceRoot),
            CreatePermissionSettings());

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
        ConversationExecutionPhase executionPhase = ConversationExecutionPhase.Execution,
        string toolName = "tool")
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);

        return new ToolExecutionContext(
            "call_1",
            toolName,
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

    private static PermissionSettings CreatePermissionSettings(PermissionSettings? settings = null)
    {
        return settings ?? new PermissionSettings
        {
            DefaultMode = PermissionMode.Ask,
            Rules = []
        };
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
}
