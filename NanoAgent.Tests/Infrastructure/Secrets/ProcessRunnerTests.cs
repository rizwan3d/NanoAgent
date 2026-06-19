using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.WindowsSandbox;

namespace NanoAgent.Tests.Infrastructure.Secrets;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_AttachPseudoTerminal_When_RequestedAndSupported()
    {
        ProcessExecutionRequest? request = CreatePseudoTerminalProbeRequest();
        if (request is null)
        {
            return;
        }

        ProcessExecutionResult result;
        try
        {
            result = await new ProcessRunner().RunAsync(
                request,
                CancellationToken.None);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("terminal");
    }

    [Fact]
    public async Task RunAsync_Should_CapCapturedStandardOutputAndError()
    {
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "[Console]::Out.Write(('o' * 20000)); [Console]::Error.Write(('e' * 20000))"
                ],
                MaxOutputCharacters: 128)
            : new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    "printf '%*s' 20000 '' | tr ' ' o; printf '%*s' 20000 '' | tr ' ' e >&2"
                ],
                MaxOutputCharacters: 128);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Length.Should().BeLessThanOrEqualTo(128);
        result.StandardError.Length.Should().BeLessThanOrEqualTo(128);
        result.StandardOutput.Should().EndWith("...");
        result.StandardError.Should().EndWith("...");
    }

    [Fact]
    public async Task RunAsync_Should_ForwardStandardInput_DirectExecution()
    {
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "$value = [Console]::In.ReadToEnd(); [Console]::Out.Write($value)"
                ],
                StandardInput: "stdin-ok")
            : new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    "cat"
                ],
                StandardInput: "stdin-ok");

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be("stdin-ok");
        result.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Should_UseWorkingDirectory_ForDirectExecution()
    {
        using TempWorkspace temp = new();
        string workingDirectory = Path.Combine(temp.WorkspaceRoot, "nested");
        Directory.CreateDirectory(workingDirectory);
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "[Console]::Out.Write((Get-Location).Path)"
                ],
                WorkingDirectory: workingDirectory)
            : new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    "pwd"
                ],
                WorkingDirectory: workingDirectory);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        string.Equals(
            result.StandardOutput.Trim(),
            workingDirectory,
            StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Should_PassEnvironmentVariables_ForDirectExecution()
    {
        const string variableName = "NANOAGENT_PROCESS_RUNNER_TEST";
        const string variableValue = "env-ok";
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    $"[Console]::Out.Write($env:{variableName})"
                ],
                EnvironmentVariables: new Dictionary<string, string>
                {
                    [variableName] = variableValue
                })
            : new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    $"printf %s \"${variableName}\""
                ],
                EnvironmentVariables: new Dictionary<string, string>
                {
                    [variableName] = variableValue
                });

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be(variableValue);
        result.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Should_RejectEnvironmentVariablesWithEmbeddedNulls_ForDirectExecution()
    {
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "cmd.exe",
                ["/c", "exit /b 0"],
                EnvironmentVariables: new Dictionary<string, string>
                {
                    ["NANOAGENT_PROCESS_RUNNER_TEST"] = "safe\0EVIL=malicious"
                })
            : new ProcessExecutionRequest(
                "/bin/sh",
                ["-c", "true"],
                EnvironmentVariables: new Dictionary<string, string>
                {
                    ["NANOAGENT_PROCESS_RUNNER_TEST"] = "safe\0EVIL=malicious"
                });

        Func<Task> act = () => new ProcessRunner().RunAsync(request, CancellationToken.None);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*NANOAGENT_PROCESS_RUNNER_TEST*embedded null*");
    }

    [Fact]
    public async Task RunAsync_Should_PreserveExitCodeAndStandardError_ForDirectExecution()
    {
        ProcessExecutionRequest request = OperatingSystem.IsWindows()
            ? new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "[Console]::Error.Write('stderr-ok'); exit 7"
                ])
            : new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    "printf %s stderr-ok >&2; exit 7"
                ]);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(
            request,
            CancellationToken.None);

        result.ExitCode.Should().Be(7);
        result.StandardOutput.Should().BeEmpty();
        result.StandardError.Should().Contain("stderr-ok");
    }

    [Fact]
    public async Task RunAsync_Should_RunRealNodeVersionInsideWindowsSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? nodePath = FindNodePath();
        if (string.IsNullOrWhiteSpace(nodePath))
        {
            return;
        }

        using TempWorkspace temp = new();
        string nanoAgentHome = WindowsSandboxPaths.ResolveAppHome();
        string materializedNodePath = WindowsSandboxHelperMaterializer.MaterializeExternalExecutable(
            nanoAgentHome,
            nodePath);
        string scriptPath = Path.Combine(temp.WorkspaceRoot, "run-node-version.cmd");
        File.WriteAllText(
            scriptPath,
            $"@echo off{Environment.NewLine}\"{materializedNodePath}\" -v{Environment.NewLine}");
        WindowsSandboxExecutionContext context = new(
            ToolSandboxMode.WorkspaceWrite,
            nanoAgentHome,
            temp.WorkspaceRoot,
            temp.WorkspaceRoot,
            [temp.WorkspaceRoot],
            IncludeTempEnvironmentVariables: true);
        ProcessExecutionRequest request = new(
            "cmd.exe",
            ["/c", scriptPath],
            WorkingDirectory: temp.WorkspaceRoot,
            MaxOutputCharacters: 256);

        using CancellationTokenSource timeout = CreateWindowsSandboxTimeout();
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(0, $"stdout={result.StandardOutput} stderr={result.StandardError}");
        result.StandardError.Should().BeNullOrWhiteSpace();
        result.StandardOutput.Trim().Should().MatchRegex(@"^v\d+\.\d+\.\d+");
    }

    [Fact]
    public async Task RunAsync_Should_RunCmdEchoInsideWindowsSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempWorkspace temp = new();
        WindowsSandboxExecutionContext context = new(
            ToolSandboxMode.WorkspaceWrite,
            WindowsSandboxPaths.ResolveAppHome(),
            temp.WorkspaceRoot,
            temp.WorkspaceRoot,
            [temp.WorkspaceRoot],
            IncludeTempEnvironmentVariables: true);
        ProcessExecutionRequest request = new(
            "cmd.exe",
            ["/c", "echo sandbox-ok"],
            WorkingDirectory: temp.WorkspaceRoot,
            MaxOutputCharacters: 256);

        using CancellationTokenSource timeout = CreateWindowsSandboxTimeout();
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(0, $"stdout={result.StandardOutput} stderr={result.StandardError}");
        result.StandardError.Should().BeNullOrWhiteSpace();
        result.StandardOutput.Should().Contain("sandbox-ok");
    }

    [Fact]
    public async Task RunAsync_Should_UseWorkingDirectoryInsideWindowsSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempWorkspace temp = new();
        string workingDirectory = Path.Combine(temp.WorkspaceRoot, "nested");
        Directory.CreateDirectory(workingDirectory);
        WindowsSandboxExecutionContext context = CreateWindowsSandboxExecutionContext(temp.WorkspaceRoot);
        ProcessExecutionRequest request = new(
            "cmd.exe",
            ["/c", "cd"],
            WorkingDirectory: workingDirectory,
            MaxOutputCharacters: 256);

        using CancellationTokenSource timeout = CreateWindowsSandboxTimeout();
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(0, $"stdout={result.StandardOutput} stderr={result.StandardError}");
        result.StandardError.Should().BeNullOrWhiteSpace();
        string.Equals(
            result.StandardOutput.Trim(),
            workingDirectory,
            StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Should_PassCustomEnvironmentVariablesInsideWindowsSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempWorkspace temp = new();
        WindowsSandboxExecutionContext context = CreateWindowsSandboxExecutionContext(temp.WorkspaceRoot);
        string variableName = "NANOAGENT_SANDBOX_TEST_VALUE";
        string variableValue = "sandbox-env-ok";
        ProcessExecutionRequest request = new(
            "cmd.exe",
            ["/c", $"echo %{variableName}%"],
            WorkingDirectory: temp.WorkspaceRoot,
            MaxOutputCharacters: 256,
            EnvironmentVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [variableName] = variableValue
            });

        using CancellationTokenSource timeout = CreateWindowsSandboxTimeout();
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(0, $"stdout={result.StandardOutput} stderr={result.StandardError}");
        result.StandardError.Should().BeNullOrWhiteSpace();
        result.StandardOutput.Trim().Should().Be(variableValue);
    }

    [Fact]
    public void BuildEnvironmentBlockBytes_Should_RejectEnvironmentValuesWithEmbeddedNulls()
    {
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NANOAGENT_SANDBOX_JUSTIFICATION"] = "safe\0EVIL=malicious"
        };

        Action act = () => WindowsSandboxProcessRunner.BuildEnvironmentBlockBytes(environment);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*NANOAGENT_SANDBOX_JUSTIFICATION*embedded null*");
    }

    [Fact]
    public void BuildEnvironmentBlockBytes_Should_RejectEnvironmentNamesWithEmbeddedNulls()
    {
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NANOAGENT_SANDBOX_JUSTIFICATION\0EVIL"] = "malicious"
        };

        Action act = () => WindowsSandboxProcessRunner.BuildEnvironmentBlockBytes(environment);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*NANOAGENT_SANDBOX_JUSTIFICATION*embedded null*");
    }

    [Fact]
    public async Task RunAsync_Should_ForwardStandardInputInsideWindowsSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempWorkspace temp = new();
        WindowsSandboxExecutionContext context = CreateWindowsSandboxExecutionContext(temp.WorkspaceRoot);
        ProcessExecutionRequest request = new(
            "powershell",
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$value = [Console]::In.ReadToEnd(); [Console]::Out.Write($value)"
            ],
            StandardInput: "sandbox-stdin-ok",
            WorkingDirectory: temp.WorkspaceRoot,
            MaxOutputCharacters: 256);

        using CancellationTokenSource timeout = CreateWindowsSandboxTimeout();
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(0, $"stdout={result.StandardOutput} stderr={result.StandardError}");
        result.StandardError.Should().BeNullOrWhiteSpace();
        result.StandardOutput.Should().Be("sandbox-stdin-ok");
    }

    [Fact]
    public async Task RunAsync_Should_PreserveStandardErrorAndExitCodeInsideWindowsSandbox()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempWorkspace temp = new();
        WindowsSandboxExecutionContext context = CreateWindowsSandboxExecutionContext(temp.WorkspaceRoot);
        ProcessExecutionRequest request = new(
            "cmd.exe",
            ["/c", "echo sandbox-stderr 1>&2 & exit /b 7"],
            WorkingDirectory: temp.WorkspaceRoot,
            MaxOutputCharacters: 256);

        using CancellationTokenSource timeout = CreateWindowsSandboxTimeout();
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(7);
        result.StandardOutput.Should().BeNullOrWhiteSpace();
        result.StandardError.Should().Contain("sandbox-stderr");
    }

    [Fact]
    public async Task ExecuteWithStartupRetryAsync_Should_RetryStatusDllInitFailedOnce()
    {
        using TempNanoAgentHome nanoAgentHome = new();
        int attempts = 0;

        ProcessExecutionResult result = await WindowsSandboxProcessRunner.ExecuteWithStartupRetryAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(
                    attempts == 1
                        ? new ProcessExecutionResult(
                            WindowsSandboxProcessRunner.StatusDllInitFailed,
                            string.Empty,
                            string.Empty)
                        : new ProcessExecutionResult(
                            0,
                            "ok",
                            string.Empty));
            },
            nanoAgentHome.Path,
            CancellationToken.None);

        attempts.Should().Be(2);
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be("ok");
    }

    [Fact]
    public async Task ExecuteWithStartupRetryAsync_Should_NotRetrySuccessfulLaunches()
    {
        using TempNanoAgentHome nanoAgentHome = new();
        int attempts = 0;

        ProcessExecutionResult result = await WindowsSandboxProcessRunner.ExecuteWithStartupRetryAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(new ProcessExecutionResult(0, "ok", string.Empty));
            },
            nanoAgentHome.Path,
            CancellationToken.None);

        attempts.Should().Be(1);
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be("ok");
    }

    [Fact]
    public void EnsureSandboxRuntimeLayout_Should_CreateCommonPowerShellAndNodeDirectories()
    {
        using TempNanoAgentHome nanoAgentHome = new();

        WindowsSandboxProcessRunner.WindowsSandboxRuntimeLayout layout =
            WindowsSandboxProcessRunner.EnsureSandboxRuntimeLayout(nanoAgentHome.Path);

        layout.WritableDirectories.Should().NotBeEmpty();
        Directory.Exists(layout.ProfileDir).Should().BeTrue();
        Directory.Exists(layout.TempDir).Should().BeTrue();
        Directory.Exists(layout.RoamingDir).Should().BeTrue();
        Directory.Exists(layout.LocalAppDataDir).Should().BeTrue();
        Directory.Exists(layout.WindowsPowerShellModulesDir).Should().BeTrue();
        Directory.Exists(layout.PowerShellModulesDir).Should().BeTrue();
        Directory.Exists(layout.NpmCacheDir).Should().BeTrue();
        Directory.Exists(layout.CorepackHomeDir).Should().BeTrue();
    }

    private static ProcessExecutionRequest? CreatePseudoTerminalProbeRequest()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Environment.OSVersion.Version.Build < 17763)
            {
                return null;
            }

            return new ProcessExecutionRequest(
                "powershell",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "if ([Console]::IsOutputRedirected) { 'redirected' } else { 'terminal' }"
                ],
                MaxOutputCharacters: 1024,
                UsePseudoTerminal: true);
        }

        if (OperatingSystem.IsLinux())
        {
            if (!File.Exists("/usr/bin/script") &&
                !File.Exists("/bin/script"))
            {
                return null;
            }

            return new ProcessExecutionRequest(
                "/bin/sh",
                [
                    "-c",
                    "if [ -t 1 ]; then printf terminal; else printf redirected; fi"
                ],
                MaxOutputCharacters: 1024,
                UsePseudoTerminal: true);
        }

        return null;
    }

    private static string? FindNodePath()
    {
        string[] candidates =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "nodejs",
                "node.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "nodejs",
                "node.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static WindowsSandboxExecutionContext CreateWindowsSandboxExecutionContext(string workspaceRoot)
    {
        return new WindowsSandboxExecutionContext(
            ToolSandboxMode.WorkspaceWrite,
            WindowsSandboxPaths.ResolveAppHome(),
            workspaceRoot,
            workspaceRoot,
            [workspaceRoot],
            IncludeTempEnvironmentVariables: true);
    }

    private static CancellationTokenSource CreateWindowsSandboxTimeout()
    {
        return new CancellationTokenSource(TimeSpan.FromMinutes(3));
    }

    private sealed class TempNanoAgentHome : IDisposable
    {
        public TempNanoAgentHome()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nanoagent-sandbox-home-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            WorkspaceRoot = Path.Combine(Path.GetTempPath(), "nanoagent-sandbox-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(WorkspaceRoot);
        }

        public string WorkspaceRoot { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(WorkspaceRoot, recursive: true);
            }
            catch
            {
            }
        }
    }
}
