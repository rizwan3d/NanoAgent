using System.Runtime.InteropServices;
using FluentAssertions;
using StemCode.Infrastructure.Workspaces;

namespace StemCode.Tests.Infrastructure;

/// <summary>
/// Characterization tests for <see cref="WorkspaceIgnoreMatcher"/>. The matcher
/// implements gitignore-style ignore semantics; these tests lock in that behavior
/// for the span-based, allocation-free rewrite so a regression in matching cannot
/// silently reintroduce the old per-call Regex/Split/ToArray allocations.
/// </summary>
public sealed class WorkspaceIgnoreMatcherTests
{
    // ------------------------------------------------------------------
    // MatchesGlob: the file-glob / include-exclude matching path
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("*.log", "a/b.log", false, true)]
    [InlineData("*.log", "a/b.txt", false, false)]
    [InlineData("*.log", "build.log", false, true)]
    [InlineData("*.log", "a.log", true, true)]
    [InlineData("*.log", "a.log", false, true)]
    [InlineData("*.log", "a.log/b/c", false, true)]
    [InlineData("foo?.txt", "foo1.txt", false, true)]
    [InlineData("foo?.txt", "foo12.txt", false, false)]
    [InlineData("*.tmp", "sub/x.tmp", false, true)]
    [InlineData("src/*.log", "src/a.log", false, true)]
    [InlineData("src/*.log", "other/a.log", false, false)]
    [InlineData("/src/*.log", "src/a.log", false, true)]
    [InlineData("/src/*.log", "other/a.log", false, false)]
    [InlineData("**/*.log", "a/b.log", false, true)]
    [InlineData("**/*.log", "a/b/c.log", false, true)]
    [InlineData("src/**", "src/a/b", false, true)]
    [InlineData("src/**", "other/a/b", false, false)]
    [InlineData("build/", "build", true, true)]
    [InlineData("build/", "build", false, false)]
    [InlineData("build/", "build/a.txt", false, true)]
    [InlineData("src/build/", "src/build/a.o", false, true)]
    [InlineData("src/build/", "src/build", true, true)]
    [InlineData("src/build/", "src/build", false, false)]
    [InlineData("src/build/", "src/buildx/a.o", false, false)]
    [InlineData("[abc].txt", "a.txt", false, true)]
    [InlineData("[abc].txt", "d.txt", false, false)]
    [InlineData("[!a].txt", "b.txt", false, true)]
    [InlineData("[!a].txt", "a.txt", false, false)]
    [InlineData("**/", "a/b", false, true)]
    [InlineData("**/", "a", true, true)]
    [InlineData("**/", "a", false, false)]
    public void MatchesGlob_Should_ApplyGitignoreSemantics(
        string pattern,
        string relativePath,
        bool isDirectory,
        bool expected)
    {
        WorkspaceIgnoreMatcher.MatchesGlob(pattern, relativePath, isDirectory)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void MatchesGlob_Should_ReturnFalse_ForEmptyPattern(string pattern)
    {
        WorkspaceIgnoreMatcher.MatchesGlob(pattern, "a/b.log", isDirectory: false)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("*.log", "")]
    [InlineData("*.log", "   ")]
    [InlineData("*.log", null!)]
    public void MatchesGlob_Should_ReturnFalse_ForEmptyPath(
        string pattern,
        string relativePath)
    {
        WorkspaceIgnoreMatcher.MatchesGlob(pattern, relativePath, isDirectory: false)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void MatchesGlob_Should_HonorCaseInsensitivityOnWindows()
    {
        // On Windows gitignore matching is case-insensitive; elsewhere it is not.
        bool expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        WorkspaceIgnoreMatcher.MatchesGlob("*.LOG", "a/b.log", isDirectory: false)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void MatchesGlob_Should_MatchCharacterClassRangeCaseInsensitivelyOnWindows()
    {
        bool expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        WorkspaceIgnoreMatcher.MatchesGlob("[a-z].txt", "M.txt", isDirectory: false)
            .Should()
            .Be(expected);
    }

    // ------------------------------------------------------------------
    // CompiledGlob: precompiled, allocation-free per-candidate matching
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("*.log", "a/b.log", false, true)]
    [InlineData("src/*.log", "other/a.log", false, false)]
    [InlineData("**/*.log", "a/b/c.log", false, true)]
    [InlineData("src/**", "src/a/b", false, true)]
    [InlineData("build/", "build/a.txt", false, true)]
    [InlineData("[abc].txt", "a.txt", false, true)]
    [InlineData("[!a].txt", "a.txt", false, false)]
    [InlineData("**/", "a/b", false, true)]
    public void CompiledGlob_Matches_Should_AgreeWithMatchesGlob(
        string pattern,
        string relativePath,
        bool isDirectory,
        bool expected)
    {
        WorkspaceIgnoreMatcher.CompiledGlob glob = WorkspaceIgnoreMatcher.CompiledGlob.Parse(pattern);

        glob.IsValid.Should().BeTrue();
        glob.Matches(relativePath.AsSpan(), isDirectory)
            .Should()
            .Be(expected);

        // The compiled form must agree with the per-call parse path so the
        // precompiled optimization cannot silently diverge in behavior.
        WorkspaceIgnoreMatcher.MatchesGlob(pattern, relativePath, isDirectory)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void CompiledGlob_Parse_Should_ProduceInvalidGlobForEmptyPattern(string pattern)
    {
        WorkspaceIgnoreMatcher.CompiledGlob glob = WorkspaceIgnoreMatcher.CompiledGlob.Parse(pattern);

        glob.IsValid.Should().BeFalse();
        glob.Matches("a/b.log".AsSpan(), isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void CompiledGlob_Matches_Should_HonorCaseInsensitivityOnWindows()
    {
        bool expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        WorkspaceIgnoreMatcher.CompiledGlob.Parse("*.LOG")
            .Matches("a/b.log".AsSpan(), isDirectory: false)
            .Should()
            .Be(expected);
    }

    // ------------------------------------------------------------------
    // Load / IsIgnored: file-driven rules, negation, nested base-relative
    // ------------------------------------------------------------------

    [Fact]
    public void Load_Should_IgnoreMatchedPathsAndHonorNegation()
    {
        string root = CreateTempWorkspace(
            (".gitignore", "*.log\n!keep.log\n"));

        try
        {
            WorkspaceIgnoreMatcher matcher = WorkspaceIgnoreMatcher.Load(
                root,
                [".gitignore"]);

            matcher.HasRules.Should().BeTrue();
            matcher.IsIgnored(Path.Combine(root, "a", "b.log"), isDirectory: false).Should().BeTrue();
            matcher.IsIgnored(Path.Combine(root, "keep.log"), isDirectory: false).Should().BeFalse();
            matcher.IsIgnored(Path.Combine(root, "a", "keep.log"), isDirectory: false).Should().BeFalse();
            matcher.IsIgnored(Path.Combine(root, "a", "b.txt"), isDirectory: false).Should().BeFalse();

            matcher.TryGetIgnoreSource(
                Path.Combine(root, "a", "b.log"),
                isDirectory: false,
                out string source).Should().BeTrue();
            source.Should().Be(".gitignore");
        }
        finally
        {
            DeleteTempWorkspace(root);
        }
    }

    [Fact]
    public void Load_Should_AnchorNestedIgnoreRulesToTheirDirectory()
    {
        string root = CreateTempWorkspace(
            (".gitignore", "*.log\n"),
            (Path.Combine("sub", ".gitignore"), "*.tmp\n"));

        try
        {
            WorkspaceIgnoreMatcher matcher = WorkspaceIgnoreMatcher.Load(
                root,
                [".gitignore", Path.Combine("sub", ".gitignore")]);

            // Root rule applies everywhere.
            matcher.IsIgnored(Path.Combine(root, "a", "b.log"), isDirectory: false).Should().BeTrue();

            // Nested rule is anchored to sub/ and matches recursively beneath it.
            matcher.IsIgnored(Path.Combine(root, "sub", "x.tmp"), isDirectory: false).Should().BeTrue();
            matcher.IsIgnored(Path.Combine(root, "sub", "deep", "y.tmp"), isDirectory: false).Should().BeTrue();

            // Nested rule does not leak outside sub/.
            matcher.IsIgnored(Path.Combine(root, "x.tmp"), isDirectory: false).Should().BeFalse();
            matcher.IsIgnored(Path.Combine(root, "other", "z.tmp"), isDirectory: false).Should().BeFalse();
        }
        finally
        {
            DeleteTempWorkspace(root);
        }
    }

    [Fact]
    public void Load_Should_ReturnEmptyMatcher_WhenNoIgnoreFilesExist()
    {
        string root = CreateTempWorkspace();
        try
        {
            WorkspaceIgnoreMatcher matcher = WorkspaceIgnoreMatcher.Load(
                root,
                [".gitignore"]);

            matcher.HasRules.Should().BeFalse();
            matcher.IsIgnored(Path.Combine(root, "a", "b.log"), isDirectory: false).Should().BeFalse();
        }
        finally
        {
            DeleteTempWorkspace(root);
        }
    }

    [Fact]
    public void LoadWithProjectIgnoreRules_Should_LoadGitignoreAndStemCodeIgnore()
    {
        string root = CreateTempWorkspace(
            (".gitignore", "*.log\n"),
            // .stemcodeignore rules apply workspace-wide (root-relative), not under .stemcode/.
            (Path.Combine(".stemcode", ".stemcodeignore"), "secret/**\n"));

        try
        {
            WorkspaceIgnoreMatcher matcher = WorkspaceIgnoreMatcher.LoadWithProjectIgnoreRules(root);

            matcher.HasRules.Should().BeTrue();
            matcher.IsIgnored(Path.Combine(root, "a", "b.log"), isDirectory: false).Should().BeTrue();
            matcher.IsIgnored(Path.Combine(root, "secret", "key"), isDirectory: false).Should().BeTrue();
            matcher.IsIgnored(Path.Combine(root, ".stemcode", "secret", "key"), isDirectory: false)
                .Should()
                .BeFalse();
        }
        finally
        {
            DeleteTempWorkspace(root);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string CreateTempWorkspace(params (string RelativePath, string Contents)[] files)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "stemcode-ignore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        foreach ((string relativePath, string contents) in files)
        {
            string fullPath = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        return root;
    }

    private static void DeleteTempWorkspace(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; ignore locked-file races on CI.
        }
    }
}
