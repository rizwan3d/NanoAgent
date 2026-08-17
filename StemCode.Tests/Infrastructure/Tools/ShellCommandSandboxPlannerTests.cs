using FluentAssertions;
using StemCode.Application.Models;
using StemCode.Infrastructure.Secrets;
using StemCode.Infrastructure.Tools;
using StemCode.Infrastructure.Workspaces;

namespace StemCode.Tests.Infrastructure.Tools;

public sealed class ShellCommandSandboxPlannerTests : IDisposable
{
    private readonly string _workspaceRoot;

    public ShellCommandSandboxPlannerTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"StemCode-Sandbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        WorkspaceRestrictedPathPolicy.ClearCache();
    }

    [Fact]
    public void Create_Should_NotBindHostRoot_OnLinux()
    {
        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);

        plan.Enforcement.Should().Be(ShellCommandSandboxPlanner.BubblewrapEnforcement);
        plan.Request.FileName.Should().Be("bwrap");

        // The whole host filesystem must never be exposed.
        IndexOfSequence(plan.Request.Arguments, ["--ro-bind", "/", "/"]).Should().Be(-1);
        IndexOfSequence(plan.Request.Arguments, ["--ro-bind-try", "/", "/"]).Should().Be(-1);
        IndexOfSequence(plan.Request.Arguments, ["--bind", "/", "/"]).Should().Be(-1);
    }

    [Fact]
    public void Create_Should_ExposeOnlyRequiredSystemRootsAndWorkspace_OnLinux()
    {
        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);
        IReadOnlyList<string> arguments = plan.Request.Arguments;

        foreach (string systemRoot in new[] { "/usr", "/bin", "/lib", "/etc" })
        {
            IndexOfSequence(arguments, ["--ro-bind-try", systemRoot, systemRoot])
                .Should()
                .BeGreaterThan(-1, $"{systemRoot} is required to run commands");
        }

        IndexOfSequence(arguments, ["--bind", _workspaceRoot, _workspaceRoot])
            .Should()
            .BeGreaterThan(-1);
        arguments.Should().Contain("--tmpfs").And.Contain("/tmp");
    }

    [Fact]
    public void Create_Should_NotExposeSensitiveHomeDirectories_OnLinux()
    {
        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);

        string home = SandboxHostPaths.ResolveHomeDirectory();
        if (string.IsNullOrWhiteSpace(home))
        {
            return;
        }

        foreach (string sensitive in SandboxHostPaths.SensitiveHomeEntries)
        {
            string sensitivePath = Path.Combine(home, sensitive);
            plan.Request.Arguments
                .Should()
                .NotContain(sensitivePath, $"{sensitive} holds credentials");
        }
    }

    [Fact]
    public void Create_Should_BindWorkspaceReadOnly_When_ModeIsReadOnly_OnLinux()
    {
        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.ReadOnly);

        IndexOfSequence(plan.Request.Arguments, ["--ro-bind", _workspaceRoot, _workspaceRoot])
            .Should()
            .BeGreaterThan(-1);
        IndexOfSequence(plan.Request.Arguments, ["--bind", _workspaceRoot, _workspaceRoot])
            .Should()
            .Be(-1);
    }

    [Fact]
    public void Create_Should_MaskRestrictedPathsAfterWorkspaceMount_OnLinux()
    {
        WriteStemCodeIgnore(
            """
            .env
            *.secret
            .stemcode/secrets/
            """);
        WriteFile(".env", "TOKEN=1");
        WriteFile("api.secret", "value");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".stemcode", "secrets"));
        WriteFile(Path.Combine(".stemcode", "secrets", "token.txt"), "secret");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);
        IReadOnlyList<string> arguments = plan.Request.Arguments;

        string envPath = Path.Combine(_workspaceRoot, ".env");
        string secretPath = Path.Combine(_workspaceRoot, "api.secret");
        string secretsDirectory = Path.Combine(_workspaceRoot, ".stemcode", "secrets");

        IndexOfSequence(arguments, ["--ro-bind", "/dev/null", envPath]).Should().BeGreaterThan(-1);
        IndexOfSequence(arguments, ["--ro-bind", "/dev/null", secretPath]).Should().BeGreaterThan(-1);
        IndexOfSequence(arguments, ["--tmpfs", secretsDirectory]).Should().BeGreaterThan(-1);

        // bubblewrap applies mounts in order, so masks must come after the workspace mount.
        int workspaceIndex = IndexOfSequence(arguments, ["--bind", _workspaceRoot, _workspaceRoot]);
        IndexOfSequence(arguments, ["--ro-bind", "/dev/null", envPath])
            .Should()
            .BeGreaterThan(workspaceIndex);
        IndexOfSequence(arguments, ["--tmpfs", secretsDirectory])
            .Should()
            .BeGreaterThan(workspaceIndex);
    }

    [Fact]
    public void Create_Should_NotMaskPathsMissingFromStemCodeIgnore_OnLinux()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".git"));
        WriteFile(Path.Combine(".git", "config"), "[core]");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);

        string gitDirectory = Path.Combine(_workspaceRoot, ".git");
        IndexOfSequence(plan.Request.Arguments, ["--tmpfs", gitDirectory]).Should().Be(-1);
        plan.ResolvedRestrictedReadPaths.Should().BeEquivalentTo(["api.secret"]);
    }

    [Fact]
    public void Create_Should_NotMaskAnything_When_NoIgnoreFileExists_OnLinux()
    {
        WriteFile("app.cs", "code");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);

        plan.ResolvedRestrictedReadPaths.Should().BeEmpty();
        IndexOfSequence(plan.Request.Arguments, ["--ro-bind", "/dev/null"]).Should().Be(-1);
    }

    [Fact]
    public void Create_Should_PlaceCommandAfterSandboxArguments_OnLinux()
    {
        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.WorkspaceWrite);
        IReadOnlyList<string> arguments = plan.Request.Arguments;

        int chdirIndex = arguments.ToList().IndexOf("--chdir");
        chdirIndex.Should().BeGreaterThan(-1);
        arguments[chdirIndex + 1].Should().Be(_workspaceRoot);
        arguments[chdirIndex + 2].Should().Be("/bin/bash");
        arguments[chdirIndex + 3].Should().Be("-lc");
        arguments[chdirIndex + 4].Should().Be("echo hi");
    }

    [Fact]
    public void Create_Should_EmitFileReadDenyRules_OnMacOs()
    {
        WriteStemCodeIgnore(
            """
            .env
            .stemcode/secrets/
            """);
        WriteFile(".env", "TOKEN=1");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".stemcode", "secrets"));

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.MacOs,
            ToolSandboxMode.WorkspaceWrite);

        plan.Enforcement.Should().Be(ShellCommandSandboxPlanner.SandboxExecEnforcement);
        plan.Request.FileName.Should().Be("sandbox-exec");

        string profile = plan.Request.Arguments[1];
        profile.Should().Contain("(deny file-read*");
        profile.Should().Contain($"(literal \"{EscapeForProfile(Path.Combine(_workspaceRoot, ".env"))}\")");
        profile.Should().Contain(
            $"(subpath \"{EscapeForProfile(Path.Combine(_workspaceRoot, ".stemcode", "secrets"))}\")");
    }

    [Fact]
    public void Create_Should_EmitDenyRulesAfterAllowRules_OnMacOs()
    {
        WriteStemCodeIgnore(".env");
        WriteFile(".env", "TOKEN=1");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.MacOs,
            ToolSandboxMode.WorkspaceWrite);
        string profile = plan.Request.Arguments[1];

        // Seatbelt is last-match-wins, so the deny has to follow (allow default).
        profile.IndexOf("(deny file-read*", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(profile.IndexOf("(allow default)", StringComparison.Ordinal));
        profile.IndexOf("(deny file-read*", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(profile.IndexOf("(allow file-write*", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_Should_OmitDenyBlock_When_NothingIsRestricted_OnMacOs()
    {
        WriteFile("app.cs", "code");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.MacOs,
            ToolSandboxMode.WorkspaceWrite);
        string profile = plan.Request.Arguments[1];

        profile.Should().NotContain("(deny file-read*");
        profile.Should().Contain("(deny file-write*)");
    }

    [Fact]
    public void Create_Should_ReportRestrictedPaths_OnWindows()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Windows,
            ToolSandboxMode.WorkspaceWrite);

        plan.Enforcement.Should().Be(ShellCommandSandboxPlanner.WindowsSandboxEnforcement);
        plan.ResolvedRestrictedReadPaths.Should().BeEquivalentTo(["api.secret"]);
    }

    [Fact]
    public void Create_Should_SkipEnforcement_When_ModeIsDangerFullAccess()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Linux,
            ToolSandboxMode.DangerFullAccess);

        plan.Enforcement.Should().Be(ShellCommandSandboxPlanner.NoEnforcement);
        plan.Request.FileName.Should().Be("/bin/bash");
    }

    [Fact]
    public void Create_Should_ReportUnsupported_When_PlatformHasNoSandbox()
    {
        ShellCommandSandboxPlan plan = CreatePlan(
            ShellCommandSandboxPlatform.Unsupported,
            ToolSandboxMode.WorkspaceWrite);

        plan.Enforcement.Should().Be(ShellCommandSandboxPlanner.UnsupportedEnforcement);
        plan.IsUnsupported.Should().BeTrue();
        plan.UnsupportedReason.Should().Contain("workspace-write");
    }

    private ShellCommandSandboxPlan CreatePlan(
        ShellCommandSandboxPlatform platform,
        ToolSandboxMode mode)
    {
        ProcessExecutionRequest request = new(
            "/bin/bash",
            ["-lc", "echo hi"],
            WorkingDirectory: _workspaceRoot);

        return ShellCommandSandboxPlanner.Create(
            request,
            mode,
            _workspaceRoot,
            _workspaceRoot,
            WorkspaceRestrictedPathPolicy.Load(_workspaceRoot),
            platform);
    }

    private static string EscapeForProfile(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static int IndexOfSequence(IReadOnlyList<string> arguments, IReadOnlyList<string> sequence)
    {
        for (int index = 0; index + sequence.Count <= arguments.Count; index++)
        {
            bool matches = true;
            for (int offset = 0; offset < sequence.Count; offset++)
            {
                if (!string.Equals(arguments[index + offset], sequence[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return index;
            }
        }

        return -1;
    }

    private void WriteStemCodeIgnore(string content)
    {
        string stemCodeDirectory = Path.Combine(_workspaceRoot, ".stemcode");
        Directory.CreateDirectory(stemCodeDirectory);
        File.WriteAllText(Path.Combine(stemCodeDirectory, ".stemcodeignore"), content);
    }

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_workspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public void Dispose()
    {
        WorkspaceRestrictedPathPolicy.ClearCache();
        if (Directory.Exists(_workspaceRoot))
        {
            try
            {
                Directory.Delete(_workspaceRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup of the temporary workspace.
            }
        }
    }
}
