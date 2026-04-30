using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Tools;
using NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class ShellCommandServiceTests : IDisposable
{
    private readonly string _workspaceRoot;

    public ShellCommandServiceTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Shell-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
    }

    [Fact]
    public async Task ExecuteAsync_Should_PreserveCompoundCommands_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot));

        await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("node -v && npm -v", "src"),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        processRunner.Requests[0].MaxOutputCharacters.Should().Be(8000);

        if (OperatingSystem.IsLinux())
        {
            request.FileName.Should().Be("bwrap");
            request.WorkingDirectory.Should().Be(Path.GetFullPath(_workspaceRoot));
            ContainsArgumentSequence(request.Arguments, "--ro-bind", "/", "/").Should().BeFalse();
            request.Arguments.Should().ContainInOrder(
                "--bind",
                Path.GetFullPath(_workspaceRoot),
                Path.GetFullPath(_workspaceRoot),
                "--chdir",
                Path.Combine(_workspaceRoot, "src"),
                "/bin/bash",
                "-lc",
                "node -v && npm -v");
            request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("bubblewrap");
        }
        else if (OperatingSystem.IsMacOS())
        {
            request.FileName.Should().Be("sandbox-exec");
            request.WorkingDirectory.Should().Be(Path.Combine(_workspaceRoot, "src"));
            request.Arguments.Should().ContainInOrder("/bin/bash", "-lc", "node -v && npm -v");
            request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("sandbox-exec");
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_TranslateCompoundCommands_ForWindowsPowerShell()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.DangerFullAccess
            });

        await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("mkdir todo && cd todo && npm i", null),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.FileName.Should().Be("powershell");
        request.MaxOutputCharacters.Should().Be(8000);
        request.Arguments.Should().Contain("-Command");
        request.Arguments[^1].Should().Contain("Invoke-NanoSegment");
        request.Arguments[^1].Should().Contain("FromBase64String");
        request.Arguments[^1].Should().Contain("$__nano_exit = $__nano_segment_exit");
        request.Arguments[^1].Should().NotContain("$__nano_exit = Invoke-NanoSegment");
        request.Arguments[^1].Should().NotContain("&&");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RunRemainingWindowsSegments_When_FirstSegmentWritesOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ShellCommandService sut = new(
            new ProcessRunner(),
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.DangerFullAccess
            });

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest(
                "mkdir todo && cd todo && Set-Content marker.txt ok",
                null),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(_workspaceRoot, "todo", "marker.txt"))
            .Trim()
            .Should()
            .Be("ok");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ExposeSandboxMetadataToProcessEnvironment()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.ReadOnly
            });

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest(
                "dotnet test",
                null,
                ShellCommandSandboxPermissions.RequireEscalated,
                "needs package cache access",
                ["dotnet", "test"]),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.EnvironmentVariables.Should().NotBeNull();
        request.EnvironmentVariables!["NANOAGENT_SANDBOX_MODE"].Should().Be("read-only");
        request.EnvironmentVariables["NANOAGENT_SANDBOX_EFFECTIVE_MODE"].Should().Be("danger-full-access");
        request.EnvironmentVariables["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("none");
        request.EnvironmentVariables["NANOAGENT_SANDBOX_PERMISSIONS"].Should().Be("require_escalated");
        request.EnvironmentVariables["NANOAGENT_SANDBOX_JUSTIFICATION"].Should().Be("needs package cache access");
        request.EnvironmentVariables["NANOAGENT_SANDBOX_PREFIX_RULE"].Should().Be("dotnet test");
        request.EnvironmentVariables["NANOAGENT_WORKSPACE_ROOT"].Should().Be(Path.GetFullPath(_workspaceRoot));
        result.SandboxPermissions.Should().Be("require_escalated");
        result.Justification.Should().Be("needs package cache access");
        result.SandboxMode.Should().Be("danger-full-access");
        result.SandboxEnforcement.Should().Be("none");
    }

    [Fact]
    public async Task ExecuteAsync_Should_BypassSandboxWrapper_When_SandboxModeIsDangerFullAccess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.DangerFullAccess
            });

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("node -v && npm -v", "src"),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.FileName.Should().Be("/bin/bash");
        request.Arguments.Should().Equal("-lc", "node -v && npm -v");
        request.WorkingDirectory.Should().Be(Path.Combine(_workspaceRoot, "src"));
        request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("none");
        result.SandboxMode.Should().Be("danger-full-access");
        result.SandboxEnforcement.Should().Be("none");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ForwardPseudoTerminalToProcessRunner_When_Requested()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.DangerFullAccess
            });

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest(
                "dotnet test",
                null,
                PseudoTerminal: true),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.UsePseudoTerminal.Should().BeTrue();
        request.EnvironmentVariables!["NANOAGENT_SHELL_PTY"].Should().Be("1");
        result.PseudoTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_UseReadOnlySandbox_When_SandboxModeIsReadOnly()
    {
        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.ReadOnly
            });

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("git status --short", null),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.EnvironmentVariables!["NANOAGENT_SANDBOX_EFFECTIVE_MODE"].Should().Be("read-only");
        result.SandboxMode.Should().Be("read-only");

        if (OperatingSystem.IsLinux())
        {
            request.FileName.Should().Be("bwrap");
            request.Arguments.Should().NotContain("--bind");
            request.Arguments.Should().ContainInOrder(
                "--ro-bind",
                Path.GetFullPath(_workspaceRoot),
                Path.GetFullPath(_workspaceRoot));
            ContainsArgumentSequence(request.Arguments, "--ro-bind", "/", "/").Should().BeFalse();
            request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("bubblewrap");
        }
        else if (OperatingSystem.IsMacOS())
        {
            request.FileName.Should().Be("sandbox-exec");
            request.Arguments[1].Should().Contain("(deny file-write*)");
            request.Arguments[1].Should().NotContain("(allow file-write*");
            request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("sandbox-exec");
        }
        else if (OperatingSystem.IsWindows())
        {
            request.FileName.Should().Be("powershell");
            request.WindowsSandbox.Should().NotBeNull();
            request.WindowsSandbox!.AllowWorkspaceWrite.Should().BeFalse();
            request.WindowsSandbox.WorkspaceRoot.Should().Be(Path.GetFullPath(_workspaceRoot));
            request.WindowsSandbox.ProfileName.Should().StartWith("NanoAgent.Shell.read.");
            request.WindowsSandbox.ReadOnlyRoots.Should().Contain(GetFirstExistingPathDirectory());
            request.WindowsSandbox.ReadOnlyRoots.Should().NotContain(Path.GetFullPath(_workspaceRoot));
            request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("windows-appcontainer");
            request.EnvironmentVariables["USERPROFILE"].Should().Be(Path.GetFullPath(_workspaceRoot));
            request.EnvironmentVariables["TEMP"].Should().Be(request.WindowsSandbox.TempDirectory);
            request.EnvironmentVariables.Should().NotContainKey("TMPDIR");
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_UseWindowsAppContainerSandbox_When_SandboxModeRequiresIsolation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot));

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest("dotnet --version", null),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.FileName.Should().Be("powershell");
        request.WindowsSandbox.Should().NotBeNull();
        request.WindowsSandbox!.AllowWorkspaceWrite.Should().BeTrue();
        request.WindowsSandbox.WorkspaceRoot.Should().Be(Path.GetFullPath(_workspaceRoot));
        request.WindowsSandbox.ProfileName.Should().StartWith("NanoAgent.Shell.write.");
        request.WindowsSandbox.TempDirectory.Should().Contain("sandbox-temp");
        request.WindowsSandbox.ReadOnlyRoots.Should().Contain(GetFirstExistingPathDirectory());
        request.WindowsSandbox.ReadOnlyRoots.Should().NotContain(Path.GetFullPath(_workspaceRoot));
        request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("windows-appcontainer");
        request.EnvironmentVariables["HOME"].Should().Be(Path.GetFullPath(_workspaceRoot));
        request.EnvironmentVariables["USERPROFILE"].Should().Be(Path.GetFullPath(_workspaceRoot));
        request.EnvironmentVariables["TEMP"].Should().Be(request.WindowsSandbox.TempDirectory);
        request.EnvironmentVariables["TMP"].Should().Be(request.WindowsSandbox.TempDirectory);
        request.EnvironmentVariables.Should().NotContainKey("TMPDIR");
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be("ok");
        result.SandboxMode.Should().Be("workspace-write");
        result.SandboxEnforcement.Should().Be("windows-appcontainer");
    }

    [Fact]
    public async Task ExecuteAsync_Should_NormalizeWindowsAppContainerShellEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string existingPath = GetFirstExistingPathDirectory() ?? _workspaceRoot;
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        string? originalPwd = Environment.GetEnvironmentVariable("PWD");
        try
        {
            string providerQualifiedPath = @"Microsoft.PowerShell.Core\FileSystem::C:\Missing";
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(Path.PathSeparator, providerQualifiedPath, existingPath, existingPath));
            Environment.SetEnvironmentVariable("PWD", providerQualifiedPath);

            FakeProcessRunner processRunner = new();
            processRunner.EnqueueResult(new ProcessExecutionResult(0, "ok", string.Empty));
            ShellCommandService sut = new(
                processRunner,
                new StubWorkspaceRootProvider(_workspaceRoot));

            await sut.ExecuteAsync(
                new ShellCommandExecutionRequest("node -v", null),
                CancellationToken.None);

            processRunner.Requests.Should().ContainSingle();
            ProcessExecutionRequest request = processRunner.Requests[0];
            request.WindowsSandbox.Should().NotBeNull();
            request.EnvironmentVariables!["PWD"].Should().Be(Path.GetFullPath(_workspaceRoot));
            request.EnvironmentVariables["PATH"].Should().NotContain("Microsoft.PowerShell.Core");
            request.EnvironmentVariables["PATH"]
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Should()
                .Equal(Path.GetFullPath(existingPath));
            request.WindowsSandbox!.ReadOnlyRoots.Should().NotContain(
                path => path.Contains("Microsoft.PowerShell.Core", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PWD", originalPwd);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_UseWindowsAppContainerSandbox_When_PseudoTerminalRequested()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "terminal", string.Empty));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot));

        ShellCommandExecutionResult result = await sut.ExecuteAsync(
            new ShellCommandExecutionRequest(
                "dotnet test",
                null,
                PseudoTerminal: true),
            CancellationToken.None);

        processRunner.Requests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.Requests[0];
        request.UsePseudoTerminal.Should().BeTrue();
        request.WindowsSandbox.Should().NotBeNull();
        request.WindowsSandbox!.AllowWorkspaceWrite.Should().BeTrue();
        request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("windows-appcontainer");
        request.EnvironmentVariables["NANOAGENT_SHELL_PTY"].Should().Be("1");
        result.ExitCode.Should().Be(0);
        result.PseudoTerminal.Should().BeTrue();
        result.SandboxEnforcement.Should().Be("windows-appcontainer");
    }

    [Fact]
    public async Task StartBackgroundAsync_Should_UseWindowsAppContainerSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeProcessRunner processRunner = new();
        processRunner.EnqueueBackgroundProcess(new FakeBackgroundProcess("ready"));
        ShellCommandService sut = new(
            processRunner,
            new StubWorkspaceRootProvider(_workspaceRoot));

        ShellCommandExecutionResult result = await sut.StartBackgroundAsync(
            new ShellCommandExecutionRequest("Start-Sleep -Seconds 30", null),
            CancellationToken.None);

        processRunner.BackgroundRequests.Should().ContainSingle();
        ProcessExecutionRequest request = processRunner.BackgroundRequests[0];
        request.WindowsSandbox.Should().NotBeNull();
        request.WindowsSandbox!.AllowWorkspaceWrite.Should().BeTrue();
        request.EnvironmentVariables!["NANOAGENT_SANDBOX_ENFORCEMENT"].Should().Be("windows-appcontainer");
        request.EnvironmentVariables["TEMP"].Should().Be(request.WindowsSandbox.TempDirectory);
        result.ExitCode.Should().Be(0);
        result.Background.Should().BeTrue();
        result.TerminalAction.Should().Be("start");
        result.TerminalStatus.Should().Be("running");
        result.SandboxEnforcement.Should().Be("windows-appcontainer");
    }

    [Fact]
    public async Task BackgroundTerminal_Should_StartReadAndStopCommand()
    {
        ShellCommandService sut = new(
            new ProcessRunner(),
            new StubWorkspaceRootProvider(_workspaceRoot),
            new PermissionSettings
            {
                SandboxMode = ToolSandboxMode.DangerFullAccess
            });
        string command = OperatingSystem.IsWindows()
            ? "Write-Output ready; Start-Sleep -Seconds 30"
            : "printf ready; sleep 30";

        ShellCommandExecutionResult started = await sut.StartBackgroundAsync(
            new ShellCommandExecutionRequest(command, null),
            CancellationToken.None);

        started.Background.Should().BeTrue();
        started.TerminalId.Should().NotBeNullOrWhiteSpace();
        started.TerminalStatus.Should().Be("running");

        string terminalId = started.TerminalId!;
        try
        {
            string output = string.Empty;
            for (int attempt = 0; attempt < 20 && !output.Contains("ready", StringComparison.Ordinal); attempt++)
            {
                ShellCommandExecutionResult read = await sut.ReadBackgroundAsync(
                    terminalId,
                    CancellationToken.None);
                output += read.StandardOutput;
                await Task.Delay(50);
            }

            output.Should().Contain("ready");

            ShellCommandExecutionResult stopped = await sut.StopBackgroundAsync(
                terminalId,
                CancellationToken.None);
            stopped.TerminalStatus.Should().Be("stopped");
            stopped.ExitCode.Should().Be(0);

            ShellCommandExecutionResult missing = await sut.ReadBackgroundAsync(
                terminalId,
                CancellationToken.None);
            missing.TerminalStatus.Should().Be("not_found");
        }
        finally
        {
            await sut.StopBackgroundAsync(
                terminalId,
                CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private sealed class StubWorkspaceRootProvider : NanoAgent.Application.Abstractions.IWorkspaceRootProvider
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

    private static bool ContainsArgumentSequence(
        IReadOnlyList<string> arguments,
        params string[] expected)
    {
        if (expected.Length == 0 || arguments.Count < expected.Length)
        {
            return false;
        }

        for (int index = 0; index <= arguments.Count - expected.Length; index++)
        {
            bool matches = true;
            for (int expectedIndex = 0; expectedIndex < expected.Length; expectedIndex++)
            {
                if (!string.Equals(arguments[index + expectedIndex], expected[expectedIndex], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetFirstExistingPathDirectory()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string segment in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string fullPath = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(segment).Trim('"'));
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
