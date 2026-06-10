using FluentAssertions;
using NanoAgent.Application.Commands;

namespace NanoAgent.Tests.Application.Commands;

public sealed class CustomSlashCommandServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _userCommandsDirectory;
    private readonly string _workspaceRoot;

    public CustomSlashCommandServiceTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-custom-commands-" + Guid.NewGuid().ToString("N"));
        _workspaceRoot = Path.Combine(_tempDirectory, "workspace");
        _userCommandsDirectory = Path.Combine(_tempDirectory, "user", ".nanoagent", "commands");
        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_userCommandsDirectory);
    }

    [Fact]
    public void TryExpand_Should_ExpandFrontMatterCommandWithArguments()
    {
        WriteWorkspaceCommand(
            "security-review.md",
            """
            ---
            name: security-review
            description: Review changed files for security risks
            args: ["scope"]
            ---

            Review $scope.
            Full scope: $ARGUMENTS.
            """);

        bool found = CustomSlashCommandService.TryExpand(
            _workspaceRoot,
            "/security-review latest diff",
            out CustomSlashCommandResolution? resolution,
            out string? error,
            _userCommandsDirectory);

        found.Should().BeTrue();
        error.Should().BeNull();
        resolution.Should().NotBeNull();
        resolution!.ExpandedPrompt.Should().Be("Review latest.\nFull scope: latest diff.");
    }

    [Fact]
    public void TryExpand_Should_UseSubdirectoriesAsNamespaces()
    {
        WriteWorkspaceCommand(
            Path.Combine("review", "security.md"),
            """
            Check $ARGUMENTS.
            """);

        bool found = CustomSlashCommandService.TryExpand(
            _workspaceRoot,
            "/review:security auth flow",
            out CustomSlashCommandResolution? resolution,
            out string? error,
            _userCommandsDirectory);

        found.Should().BeTrue();
        error.Should().BeNull();
        resolution!.Command.Should().Be("/review:security");
        resolution.ExpandedPrompt.Should().Be("Check auth flow.");
    }

    [Fact]
    public void TryExpand_Should_UseProjectCommandBeforeUserCommand()
    {
        WriteUserCommand(
            "release-check.md",
            """
            User release check $ARGUMENTS.
            """);
        WriteWorkspaceCommand(
            "release-check.md",
            """
            Project release check $ARGUMENTS.
            """);

        CustomSlashCommandService.TryExpand(
                _workspaceRoot,
                "/release-check v1.2.3",
                out CustomSlashCommandResolution? resolution,
                out _,
                _userCommandsDirectory)
            .Should()
            .BeTrue();

        resolution!.ExpandedPrompt.Should().Be("Project release check v1.2.3.");
    }

    [Fact]
    public void TryExpand_Should_IgnoreReservedCommandNames()
    {
        WriteWorkspaceCommand(
            "help.md",
            """
            This should not replace built-in help.
            """);

        bool found = CustomSlashCommandService.TryExpand(
            _workspaceRoot,
            "/help",
            out CustomSlashCommandResolution? resolution,
            out string? error,
            _userCommandsDirectory);

        found.Should().BeFalse();
        resolution.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void TryExpand_Should_IgnoreBuiltInRedactCommandName()
    {
        WriteWorkspaceCommand(
            "redact.md",
            """
            This should not replace built-in redact.
            """);

        bool found = CustomSlashCommandService.TryExpand(
            _workspaceRoot,
            "/redact off",
            out CustomSlashCommandResolution? resolution,
            out string? error,
            _userCommandsDirectory);

        found.Should().BeFalse();
        resolution.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void List_Should_ReturnSuggestionMetadata()
    {
        WriteWorkspaceCommand(
            "fix-tests.md",
            """
            ---
            name: fix-tests
            description: Diagnose and fix failing tests
            args: ["target"]
            ---

            Fix $target.
            """);

        IReadOnlyList<CustomSlashCommandDescriptor> suggestions =
            CustomSlashCommandService.List(_workspaceRoot, _userCommandsDirectory);

        suggestions.Should().ContainSingle();
        suggestions[0].Command.Should().Be("/fix-tests");
        suggestions[0].Usage.Should().Be("/fix-tests <target>");
        suggestions[0].Description.Should().Be("Diagnose and fix failing tests");
        suggestions[0].RequiresArgument.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private void WriteWorkspaceCommand(string relativePath, string content)
    {
        WriteCommand(Path.Combine(_workspaceRoot, ".nanoagent", "commands"), relativePath, content);
    }

    private void WriteUserCommand(string relativePath, string content)
    {
        WriteCommand(_userCommandsDirectory, relativePath, content);
    }

    private static void WriteCommand(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
