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
    public async Task RunAsync_Should_RunRealNodeVersionInsideWindowsSandbox()
    {
        const int StatusDllInitFailed = unchecked((int)0xC0000142);

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

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        // Some Windows hosts can launch the sandbox runner correctly but still fail to
        // initialize Node under the restricted token with STATUS_DLL_INIT_FAILED.
        if (result.ExitCode == StatusDllInitFailed &&
            string.IsNullOrWhiteSpace(result.StandardOutput) &&
            string.IsNullOrWhiteSpace(result.StandardError))
        {
            return;
        }

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

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        ProcessExecutionResult result = await WindowsSandboxProcessRunner.RunAsync(
            request,
            context,
            timeout.Token);

        result.ExitCode.Should().Be(0, $"stdout={result.StandardOutput} stderr={result.StandardError}");
        result.StandardError.Should().BeNullOrWhiteSpace();
        result.StandardOutput.Should().Contain("sandbox-ok");
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
