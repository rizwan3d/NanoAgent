using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class FilePathSuggestionProviderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public FilePathSuggestionProviderTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-path-suggestions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public void GetSuggestions_Should_SuggestWorkspaceFilesForReadCommand()
    {
        WriteFile("README.md", "hello");
        WriteFile("docs/guide.md", "hello");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "/read R",
            maxCount: 8);

        suggestions.Should().ContainSingle();
        suggestions[0].DisplayPath.Should().Be("README.md");
        suggestions[0].CompletedInput.Should().Be("/read README.md");
        suggestions[0].IsDirectory.Should().BeFalse();
    }

    [Fact]
    public void GetSuggestions_Should_SuggestDirectoriesBeforeFiles()
    {
        WriteFile("docs/guide.md", "hello");
        WriteFile("docs.md", "hello");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "/read do",
            maxCount: 8);

        suggestions.Select(suggestion => suggestion.DisplayPath)
            .Should()
            .Equal("docs/", "docs.md");
    }

    [Fact]
    public void GetSuggestions_Should_FilterJsonFilesForImportCommand()
    {
        WriteFile("session.json", "{}");
        WriteFile("session.html", "<html></html>");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "/import se",
            maxCount: 8);

        suggestions.Select(suggestion => suggestion.DisplayPath)
            .Should()
            .Equal("session.json");
    }

    [Fact]
    public void GetSuggestions_Should_RejectPathsThatEscapeWorkspace()
    {
        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "/read ../",
            maxCount: 8);

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public void GetSuggestions_Should_SkipBuildAndRuntimeDirectories()
    {
        WriteFile("bin/output.dll", "binary");
        WriteFile(".nanoagent/cache/index.json", "{}");
        WriteFile(".nanoagent/agent-profile.json", "{}");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "/read .nanoagent/",
            maxCount: 8);

        suggestions.Select(suggestion => suggestion.DisplayPath)
            .Should()
            .Equal(".nanoagent/agent-profile.json");
    }

    [Fact]
    public void GetSuggestions_Should_CompleteDirectoryTokenForShellCommand()
    {
        WriteFile("src/index.html", "<html></html>");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!cd ./sr",
            maxCount: 8);

        suggestions.Should().ContainSingle();
        suggestions[0].DisplayPath.Should().Be("./src/");
        suggestions[0].CompletedInput.Should().Be("!cd ./src/");
        suggestions[0].IsDirectory.Should().BeTrue();
    }

    [Fact]
    public void GetSuggestions_Should_CompleteFileTokenForShellCommand()
    {
        WriteFile("index.html", "<html></html>");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!nano in",
            maxCount: 8);

        suggestions.Should().ContainSingle();
        suggestions[0].DisplayPath.Should().Be("index.html");
        suggestions[0].CompletedInput.Should().Be("!nano index.html");
        suggestions[0].IsDirectory.Should().BeFalse();
    }

    [Fact]
    public void GetSuggestions_Should_PreserveTypedDirectoryPrefixForShellCommand()
    {
        WriteFile("src/components/button.tsx", "export {}");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!cat src/comp",
            maxCount: 8);

        suggestions.Should().ContainSingle();
        suggestions[0].DisplayPath.Should().Be("src/components/");
        suggestions[0].CompletedInput.Should().Be("!cat src/components/");
    }

    [Fact]
    public void GetSuggestions_Should_CompleteTokenForBackgroundShellCommand()
    {
        WriteFile("server.js", "//");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!!node ser",
            maxCount: 8);

        suggestions.Should().ContainSingle();
        suggestions[0].CompletedInput.Should().Be("!!node server.js");
    }

    [Fact]
    public void GetSuggestions_Should_ListWorkspaceWhenShellCommandHasTrailingSpace()
    {
        WriteFile("README.md", "hello");
        WriteFile("docs/guide.md", "hello");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!ls ",
            maxCount: 8);

        suggestions.Select(suggestion => suggestion.DisplayPath)
            .Should()
            .Equal("./", "docs/", "README.md");
    }

    [Fact]
    public void GetSuggestions_Should_SuggestCurrentDirectoryForShellDotToken()
    {
        WriteFile("README.md", "hello");
        WriteFile("docs/guide.md", "hello");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!!ls .",
            maxCount: 8);

        suggestions.Select(suggestion => suggestion.DisplayPath)
            .Should()
            .Equal("./");

        suggestions[0].CompletedInput.Should().Be("!!ls ./");
        suggestions[0].Description.Should().Be("Current directory");
        suggestions[0].IsDirectory.Should().BeTrue();
    }

    [Fact]
    public void GetSuggestions_Should_NotCompleteShellCommandName()
    {
        WriteFile("cdrom.txt", "data");

        IReadOnlyList<FilePathSuggestion> suggestions = FilePathSuggestionProvider.GetSuggestions(
            _workspaceRoot,
            "!cd",
            maxCount: 8);

        suggestions.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private void WriteFile(string relativePath, string content)
    {
        string path = Path.Combine(_workspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
