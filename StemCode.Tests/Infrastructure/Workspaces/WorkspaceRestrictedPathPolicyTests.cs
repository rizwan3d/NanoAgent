using FluentAssertions;
using StemCode.Infrastructure.Workspaces;

namespace StemCode.Tests.Infrastructure.Workspaces;

public sealed class WorkspaceRestrictedPathPolicyTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceRestrictedPathPolicyTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"StemCode-Policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        WorkspaceRestrictedPathPolicy.ClearCache();
    }

    [Fact]
    public void Load_Should_ReturnEmptyPolicy_When_IgnoreFileMissing()
    {
        WriteFile("app.cs", "code");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.HasRules.Should().BeFalse();
        policy.HasRestrictions.Should().BeFalse();
        policy.RestrictedPaths.Should().BeEmpty();
    }

    [Fact]
    public void Load_Should_ResolveFilesAndDirectoriesFromStemCodeIgnore()
    {
        WriteStemCodeIgnore(
            """
            .env
            .env.*
            *.secret
            credentials.*
            *.pem
            *.key
            .stemcode/secrets/
            """);
        WriteFile(".env", "TOKEN=1");
        WriteFile(".env.production", "TOKEN=2");
        WriteFile("api.secret", "value");
        WriteFile("credentials.json", "{}");
        WriteFile("server.pem", "cert");
        WriteFile("server.key", "key");
        WriteFile("app.cs", "code");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".stemcode", "secrets"));
        WriteFile(Path.Combine(".stemcode", "secrets", "token.txt"), "secret");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.RestrictedPaths.Select(static restricted => restricted.RelativePath)
            .Should()
            .BeEquivalentTo(
            [
                ".env",
                ".env.production",
                "api.secret",
                "credentials.json",
                "server.pem",
                "server.key",
                ".stemcode/secrets"
            ]);
    }

    [Fact]
    public void Load_Should_ReportRestrictedDirectoryWithoutEnumeratingChildren()
    {
        WriteStemCodeIgnore(".stemcode/secrets/");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".stemcode", "secrets", "nested"));
        WriteFile(Path.Combine(".stemcode", "secrets", "token.txt"), "secret");
        WriteFile(Path.Combine(".stemcode", "secrets", "nested", "more.txt"), "secret");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.GetRestrictedDirectories()
            .Should()
            .BeEquivalentTo([Path.Combine(_workspaceRoot, ".stemcode", "secrets")]);
        policy.GetRestrictedFiles().Should().BeEmpty();
    }

    [Fact]
    public void IsRestricted_Should_CoverDescendantsOfRestrictedDirectory()
    {
        WriteStemCodeIgnore(".stemcode/secrets/");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".stemcode", "secrets"));
        WriteFile(Path.Combine(".stemcode", "secrets", "token.txt"), "secret");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.IsRestricted(
                Path.Combine(_workspaceRoot, ".stemcode", "secrets", "token.txt"),
                isDirectory: false)
            .Should()
            .BeTrue();
        policy.IsRestricted(
                Path.Combine(_workspaceRoot, ".stemcode", "secrets", "deep", "later.txt"),
                isDirectory: false)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsRestricted_Should_AllowPathsTheIgnoreFileDoesNotMatch()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");
        WriteFile("app.cs", "code");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".git"));
        WriteFile(Path.Combine(".git", "config"), "[core]");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.IsRestricted(Path.Combine(_workspaceRoot, "app.cs"), isDirectory: false)
            .Should()
            .BeFalse();

        // `.git` is not declared in the ignore file, so the sandbox must leave it accessible.
        policy.IsRestricted(Path.Combine(_workspaceRoot, ".git"), isDirectory: true)
            .Should()
            .BeFalse();
        policy.IsRestricted(Path.Combine(_workspaceRoot, ".git", "config"), isDirectory: false)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsRestricted_Should_MatchRuleForPathThatDoesNotExistYet()
    {
        WriteStemCodeIgnore("*.secret");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.IsRestricted(Path.Combine(_workspaceRoot, "future.secret"), isDirectory: false)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsRestricted_Should_HonorNegationRules()
    {
        WriteStemCodeIgnore(
            """
            *.secret
            !keep.secret
            """);
        WriteFile("api.secret", "value");
        WriteFile("keep.secret", "value");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.IsRestricted(Path.Combine(_workspaceRoot, "api.secret"), isDirectory: false)
            .Should()
            .BeTrue();
        policy.IsRestricted(Path.Combine(_workspaceRoot, "keep.secret"), isDirectory: false)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryGetRestrictionSource_Should_ReportIgnoreFile()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.TryGetRestrictionSource(
                Path.Combine(_workspaceRoot, "api.secret"),
                isDirectory: false,
                out string source)
            .Should()
            .BeTrue();
        source.Replace('\\', '/').Should().Be(".stemcode/.stemcodeignore");
    }

    [Fact]
    public void Load_Should_ResolveNestedMatches()
    {
        WriteStemCodeIgnore(".env");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src", "service"));
        WriteFile(Path.Combine("src", "service", ".env"), "TOKEN=1");
        WriteFile(Path.Combine("src", "service", "main.cs"), "code");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.RestrictedPaths.Select(static restricted => restricted.RelativePath)
            .Should()
            .BeEquivalentTo(["src/service/.env"]);
    }

    [Fact]
    public void Load_Should_ReturnEmptyPolicy_When_WorkspaceRootIsMissing()
    {
        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(
            Path.Combine(_workspaceRoot, "does-not-exist"));

        policy.HasRestrictions.Should().BeFalse();
        policy.IsRestricted(Path.Combine(_workspaceRoot, ".env"), isDirectory: false)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Load_Should_NotReportTruncation_ForOrdinaryWorkspace()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.Truncated.Should().BeFalse();
    }

    [Fact]
    public void LoadForSandbox_Should_SeeFilesCreatedAfterAnEarlierScan()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("first.secret", "value");

        WorkspaceRestrictedPathPolicy cached = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);
        cached.RestrictedPaths.Select(static restricted => restricted.RelativePath)
            .Should()
            .BeEquivalentTo(["first.secret"]);

        // A cached snapshot would miss this file, which the OS sandbox rules must still cover.
        WriteFile("second.secret", "value");

        WorkspaceRestrictedPathPolicy fresh = WorkspaceRestrictedPathPolicy.LoadForSandbox(_workspaceRoot);

        fresh.RestrictedPaths.Select(static restricted => restricted.RelativePath)
            .Should()
            .BeEquivalentTo(["first.secret", "second.secret"]);
    }

    [Fact]
    public void Load_Should_FindRestrictedFileBehindDirectorySymlink()
    {
        WriteStemCodeIgnore(".env");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "real"));
        WriteFile(Path.Combine("real", ".env"), "TOKEN=1");

        string linkPath = Path.Combine(_workspaceRoot, "linked");
        if (!TryCreateDirectorySymlink(linkPath, Path.Combine(_workspaceRoot, "real")))
        {
            return;
        }

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        // Sandbox mount and ACL rules are path-based, so every spelling must be restricted.
        policy.RestrictedPaths.Select(static restricted => restricted.RelativePath)
            .Should()
            .Contain("real/.env")
            .And
            .Contain("linked/.env");
        policy.IsRestricted(Path.Combine(_workspaceRoot, "real", ".env"), isDirectory: false)
            .Should()
            .BeTrue();
        policy.IsRestricted(Path.Combine(_workspaceRoot, "linked", ".env"), isDirectory: false)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Load_Should_NotLoopOnSelfReferencingSymlink()
    {
        WriteStemCodeIgnore("*.secret");
        WriteFile("api.secret", "value");

        string linkPath = Path.Combine(_workspaceRoot, "loop");
        if (!TryCreateDirectorySymlink(linkPath, _workspaceRoot))
        {
            return;
        }

        WorkspaceRestrictedPathPolicy policy = WorkspaceRestrictedPathPolicy.Load(_workspaceRoot);

        policy.Truncated.Should().BeFalse();
        policy.RestrictedPaths.Select(static restricted => restricted.RelativePath)
            .Should()
            .BeEquivalentTo(["api.secret"]);
    }

    private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Creating symlinks requires elevation or developer mode on Windows.
            return false;
        }
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
