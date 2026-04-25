using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Abstractions;
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
    [InlineData("general")]
    [InlineData("explore")]
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
            .WithMessage("*Unknown agent profile 'ops'*build*plan*review*general*explore*");
    }

    [Fact]
    public void List_Should_ReturnAllBuiltInProfiles()
    {
        BuiltInAgentProfileResolver sut = new();

        sut.List()
            .Select(static profile => profile.Name)
            .Should()
            .Equal("build", "plan", "review", "general", "explore");
    }

    [Fact]
    public void List_Should_IncludeWorkspaceAgentProfiles()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string agentsDirectory = Path.Combine(workspace.Path, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(
            Path.Combine(agentsDirectory, "qa_agent.md"),
            """
            ---
            name: qa-agent
            mode: subagent
            description: Runs focused validation.
            editMode: readOnly
            shellMode: default
            tools:
              - directory_list
              - file_read
              - shell_command
              - text_search
            permissionDescription: Read-only validation with test command execution.
            ---
            Run repo-native validation and report the failing commands.
            """);

        BuiltInAgentProfileResolver sut = new(new FixedWorkspaceRootProvider(workspace.Path));

        IAgentProfile profile = sut.Resolve("qa-agent");

        sut.List()
            .Select(static item => item.Name)
            .Should()
            .Equal("build", "plan", "review", "general", "explore", "qa-agent");
        profile.Mode.Should().Be(AgentProfileMode.Subagent);
        profile.Description.Should().Be("Runs focused validation.");
        profile.SystemPrompt.Should().Contain("Run repo-native validation");
        profile.EnabledTools.Should().Contain(AgentToolNames.ShellCommand);
        profile.EnabledTools.Should().NotContain(AgentToolNames.ApplyPatch);
        profile.PermissionIntent.EditMode.Should().Be(AgentProfileEditMode.ReadOnly);
        profile.PermissionIntent.ShellMode.Should().Be(AgentProfileShellMode.Default);
        profile.PermissionIntent.BehaviorIntent.Should().Be("Read-only validation with test command execution.");
    }

    [Fact]
    public void Resolve_Should_DeriveWorkspaceAgentNameFromFileName_When_MetadataIsMissing()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        string agentsDirectory = Path.Combine(workspace.Path, ".nanoagent", "agents");
        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(
            Path.Combine(agentsDirectory, "Security Auditor.md"),
            "Inspect for secret handling and risky shell use.");

        BuiltInAgentProfileResolver sut = new(new FixedWorkspaceRootProvider(workspace.Path));

        IAgentProfile profile = sut.Resolve("security-auditor");

        profile.Name.Should().Be("security-auditor");
        profile.Mode.Should().Be(AgentProfileMode.Subagent);
        profile.PermissionIntent.EditMode.Should().Be(AgentProfileEditMode.ReadOnly);
        profile.PermissionIntent.ShellMode.Should().Be(AgentProfileShellMode.SafeInspectionOnly);
        profile.SystemPrompt.Should().Contain("Inspect for secret handling");
    }

    [Fact]
    public void ReadOnlyProfiles_Should_NotEnableWriteTools()
    {
        BuiltInAgentProfileResolver sut = new();

        foreach (string profileName in new[] { "plan", "review", "explore" })
        {
            var profile = sut.Resolve(profileName);

            profile.EnabledTools.Should().NotContain(AgentToolNames.ApplyPatch);
            profile.EnabledTools.Should().NotContain(AgentToolNames.FileDelete);
            profile.EnabledTools.Should().NotContain(AgentToolNames.FileWrite);
            profile.EnabledTools.Should().Contain(AgentToolNames.ShellCommand);
            profile.PermissionIntent.EditMode.Should().Be(AgentProfileEditMode.ReadOnly);
            profile.PermissionIntent.ShellMode.Should().Be(AgentProfileShellMode.SafeInspectionOnly);
        }
    }

    [Fact]
    public void Profiles_Should_ExposePrimaryAndSubagentModes()
    {
        BuiltInAgentProfiles.Primary
            .Select(static profile => profile.Name)
            .Should()
            .Equal("build", "plan", "review");

        BuiltInAgentProfiles.Subagents
            .Select(static profile => profile.Name)
            .Should()
            .Equal("general", "explore");

        BuiltInAgentProfiles.General.Mode.Should().Be(AgentProfileMode.Subagent);
        BuiltInAgentProfiles.Explore.Mode.Should().Be(AgentProfileMode.Subagent);
    }

    [Fact]
    public void GeneralSubagent_Should_EnableEditToolsWithoutNestedDelegationOrLivePlanTool()
    {
        BuiltInAgentProfiles.General.EnabledTools.Should().Contain(AgentToolNames.ApplyPatch);
        BuiltInAgentProfiles.General.EnabledTools.Should().Contain(AgentToolNames.FileDelete);
        BuiltInAgentProfiles.General.EnabledTools.Should().Contain(AgentToolNames.FileWrite);
        BuiltInAgentProfiles.General.EnabledTools.Should().NotContain(AgentToolNames.AgentDelegate);
        BuiltInAgentProfiles.General.EnabledTools.Should().NotContain(AgentToolNames.UpdatePlan);
    }

    [Fact]
    public void BuildProfile_Should_DescribeNonInteractiveScaffoldingBehavior()
    {
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("fully specified, non-interactive commands");
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("agent_delegate");
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("project name, template or preset, and any confirmation flags");
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("finish the requested implementation when practical");
        BuiltInAgentProfiles.Build.SystemPrompt.Should().Contain("do not stop at analysis if you can safely continue");
    }

    [Fact]
    public void PlanProfile_Should_DescribeEvidenceBasedReadOnlyPlanning()
    {
        BuiltInAgentProfiles.Plan.SystemPrompt.Should().Contain("evidence-based implementation plan");
        BuiltInAgentProfiles.Plan.SystemPrompt.Should().Contain("separate verified facts from assumptions or open questions");
        BuiltInAgentProfiles.Plan.SystemPrompt.Should().Contain("immediate next step explicit");
        BuiltInAgentProfiles.Plan.SystemPrompt.Should().Contain("Do not patch, write files, install dependencies");
    }

    [Fact]
    public void ReviewProfile_Should_DescribeFindingsFirstReviewBehavior()
    {
        BuiltInAgentProfiles.Review.SystemPrompt.Should().Contain("Prioritize findings first");
        BuiltInAgentProfiles.Review.SystemPrompt.Should().Contain("include file or line references when practical");
        BuiltInAgentProfiles.Review.SystemPrompt.Should().Contain("say so explicitly");
        BuiltInAgentProfiles.Review.SystemPrompt.Should().Contain("testing gaps");
    }

    private sealed class FixedWorkspaceRootProvider : IWorkspaceRootProvider
    {
        private readonly string _workspaceRoot;

        public FixedWorkspaceRootProvider(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public string GetWorkspaceRoot()
        {
            return _workspaceRoot;
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorkspace Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nanoagent-agent-profile-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
