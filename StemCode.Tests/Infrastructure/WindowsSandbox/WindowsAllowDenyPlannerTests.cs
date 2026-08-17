using FluentAssertions;
using StemCode.Application.Models;
using StemCode.Infrastructure.WindowsSandbox;
using StemCode.Infrastructure.Workspaces;

namespace StemCode.Tests.Infrastructure.WindowsSandbox;

public sealed class WindowsAllowDenyPlannerTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WindowsAllowDenyPlannerTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"StemCode-WinSandbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        WorkspaceRestrictedPathPolicy.ClearCache();
    }

    [Fact]
    public void Compute_Should_DenyReadForRestrictedPaths()
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

        WindowsAllowDenyPaths paths = Compute(ToolSandboxMode.WorkspaceWrite);

        paths.DenyRead.Should().BeEquivalentTo(
        [
            Path.Combine(_workspaceRoot, ".env"),
            Path.Combine(_workspaceRoot, "api.secret"),
            Path.Combine(_workspaceRoot, ".stemcode", "secrets")
        ]);
    }

    [Fact]
    public void Compute_Should_DenyReadInReadOnlyMode()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        WindowsAllowDenyPaths paths = Compute(ToolSandboxMode.ReadOnly);

        paths.Allow.Should().BeEmpty();
        paths.DenyRead.Should().BeEquivalentTo([Path.Combine(_workspaceRoot, "api.secret")]);
    }

    [Fact]
    public void Compute_Should_MirrorDenyReadIntoDenyWrite()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        WindowsAllowDenyPaths paths = Compute(ToolSandboxMode.WorkspaceWrite);

        paths.Deny.Should().BeEquivalentTo([Path.Combine(_workspaceRoot, "api.secret")]);
    }

    [Fact]
    public void Compute_Should_NotDenyPathsMissingFromStemCodeIgnore()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".git"));
        WriteFile(Path.Combine(".git", "config"), "[core]");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".agents"));

        WindowsAllowDenyPaths paths = Compute(ToolSandboxMode.WorkspaceWrite);

        // `.git`, `.stemcode`, and `.agents` are only protected when the ignore file says so.
        paths.Deny.Should().NotContain(Path.Combine(_workspaceRoot, ".git"));
        paths.Deny.Should().NotContain(Path.Combine(_workspaceRoot, ".agents"));
        paths.DenyRead.Should().NotContain(Path.Combine(_workspaceRoot, ".git"));
        paths.DenyRead.Should().NotContain(Path.Combine(_workspaceRoot, ".agents"));
    }

    [Fact]
    public void Compute_Should_NotDenyTheWorkspaceRootItself()
    {
        WriteStemCodeIgnore(
            """
            *
            """);
        WriteFile("app.cs", "code");

        WindowsAllowDenyPaths paths = Compute(ToolSandboxMode.WorkspaceWrite);

        paths.DenyRead.Should().NotContain(_workspaceRoot);
        paths.Deny.Should().NotContain(_workspaceRoot);
    }

    [Fact]
    public void Compute_Should_ReturnNoDenials_When_IgnoreFileMissing()
    {
        WriteFile("app.cs", "code");

        WindowsAllowDenyPaths paths = Compute(ToolSandboxMode.WorkspaceWrite);

        paths.Deny.Should().BeEmpty();
        paths.DenyRead.Should().BeEmpty();
        paths.Allow.Should().Contain(_workspaceRoot);
    }

    private WindowsAllowDenyPaths Compute(ToolSandboxMode mode)
    {
        return WindowsAllowDenyPlanner.Compute(
            mode,
            _workspaceRoot,
            _workspaceRoot,
            mode == ToolSandboxMode.WorkspaceWrite ? [_workspaceRoot] : [],
            environment: null,
            includeTempEnvironmentVariables: false,
            WorkspaceRestrictedPathPolicy.Load(_workspaceRoot));
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
