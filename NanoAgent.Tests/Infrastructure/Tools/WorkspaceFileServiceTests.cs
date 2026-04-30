using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Tools;

namespace NanoAgent.Tests.Infrastructure.Tools;

public sealed class WorkspaceFileServiceTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceFileServiceTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Workspace-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task WriteFileAsync_Should_CreateAddedLinePreview_When_FileIsNew()
    {
        WorkspaceFileService sut = CreateSut();

        WorkspaceFileWriteResult result = await sut.WriteFileAsync(
            "index.html",
            "<!DOCTYPE html>\n<html lang=\"en\">\n<body>\n</body>",
            overwrite: true,
            CancellationToken.None);

        result.AddedLineCount.Should().Be(4);
        result.RemovedLineCount.Should().Be(0);
        result.PreviewLines.Should().ContainInOrder(
            new WorkspaceFileWritePreviewLine(1, "add", "<!DOCTYPE html>"),
            new WorkspaceFileWritePreviewLine(2, "add", "<html lang=\"en\">"),
            new WorkspaceFileWritePreviewLine(3, "add", "<body>"),
            new WorkspaceFileWritePreviewLine(4, "add", "</body>"));
    }

    [Fact]
    public async Task WriteFileAsync_Should_CreateContextAwarePreview_When_FileIsUpdated()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "styles.css");

        await File.WriteAllTextAsync(
            filePath,
            ".card {\n  color: red;\n}\n",
            CancellationToken.None);

        WorkspaceFileWriteResult result = await sut.WriteFileAsync(
            "styles.css",
            ".card {\n  color: blue;\n}\n",
            overwrite: true,
            CancellationToken.None);

        result.AddedLineCount.Should().Be(1);
        result.RemovedLineCount.Should().Be(1);
        result.PreviewLines.Should().ContainInOrder(
            new WorkspaceFileWritePreviewLine(1, "context", ".card {"),
            new WorkspaceFileWritePreviewLine(2, "remove", "  color: red;"),
            new WorkspaceFileWritePreviewLine(2, "add", "  color: blue;"),
            new WorkspaceFileWritePreviewLine(3, "context", "}"));
    }

    [Fact]
    public async Task WriteFileAsync_Should_AllowEmptyContent()
    {
        WorkspaceFileService sut = CreateSut();

        WorkspaceFileWriteResult result = await sut.WriteFileAsync(
            ".gitkeep",
            string.Empty,
            overwrite: true,
            CancellationToken.None);

        result.CharacterCount.Should().Be(0);
        result.AddedLineCount.Should().Be(0);
        result.PreviewLines.Should().BeEmpty();
        File.ReadAllText(Path.Combine(_workspaceRoot, ".gitkeep"))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_Should_TruncateExistingFileToEmptyContent()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "settings.json");
        await File.WriteAllTextAsync(filePath, "{}\n", CancellationToken.None);

        WorkspaceFileWriteResult result = await sut.WriteFileAsync(
            "settings.json",
            string.Empty,
            overwrite: true,
            CancellationToken.None);

        result.OverwroteExistingFile.Should().BeTrue();
        result.CharacterCount.Should().Be(0);
        File.ReadAllText(filePath).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_Should_WriteUtf8WithoutBom()
    {
        WorkspaceFileService sut = CreateSut();

        await sut.WriteFileAsync(
            "script.sh",
            "#!/bin/sh\necho hi\n",
            overwrite: true,
            CancellationToken.None);

        byte[] bytes = await File.ReadAllBytesAsync(
            Path.Combine(_workspaceRoot, "script.sh"),
            CancellationToken.None);

        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public async Task WriteFileAsync_Should_DenySymlinkDirectoryBreakout()
    {
        WorkspaceFileService sut = CreateSut();
        string outsideRoot = CreateOutsideDirectory();
        string outsideFile = Path.Combine(outsideRoot, "target.txt");
        string linkPath = Path.Combine(_workspaceRoot, "linked-outside");
        await File.WriteAllTextAsync(outsideFile, "outside", CancellationToken.None);

        try
        {
            if (!TryCreateDirectorySymlink(linkPath, outsideRoot))
            {
                return;
            }

            Func<Task> act = () => sut.WriteFileAsync(
                "linked-outside/target.txt",
                "changed",
                overwrite: true,
                CancellationToken.None);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*workspace*");
            (await File.ReadAllTextAsync(outsideFile, CancellationToken.None))
                .Should()
                .Be("outside");
        }
        finally
        {
            DeleteDirectorySymlinkIfExists(linkPath);
            DeleteDirectoryTreeIfExists(outsideRoot);
        }
    }

    [Fact]
    public async Task ReadFileAsync_Should_ReadFileContent()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "README.md");

        await File.WriteAllTextAsync(filePath, "hello", CancellationToken.None);

        WorkspaceFileReadResult result = await sut.ReadFileAsync(
            "README.md",
            CancellationToken.None);

        result.Content.Should().Be("hello");
    }

    [Fact]
    public async Task DeleteFileAsync_Should_DeleteFileAndReturnPreview()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "README.md");
        await File.WriteAllTextAsync(filePath, "first\nsecond", CancellationToken.None);

        WorkspaceFileDeleteResult result = await sut.DeleteFileAsync(
            "README.md",
            CancellationToken.None);

        File.Exists(filePath).Should().BeFalse();
        result.Path.Should().Be("README.md");
        result.DeletedCharacterCount.Should().Be(12);
        result.AddedLineCount.Should().Be(0);
        result.RemovedLineCount.Should().Be(2);
        result.PreviewLines.Should().ContainInOrder(
            new WorkspaceFileWritePreviewLine(1, "remove", "first"),
            new WorkspaceFileWritePreviewLine(2, "remove", "second"));
    }

    [Fact]
    public async Task DeleteFileAsync_Should_DenySymlinkDirectoryBreakout()
    {
        WorkspaceFileService sut = CreateSut();
        string outsideRoot = CreateOutsideDirectory();
        string outsideFile = Path.Combine(outsideRoot, "target.txt");
        string linkPath = Path.Combine(_workspaceRoot, "linked-outside");
        await File.WriteAllTextAsync(outsideFile, "outside", CancellationToken.None);

        try
        {
            if (!TryCreateDirectorySymlink(linkPath, outsideRoot))
            {
                return;
            }

            Func<Task> act = () => sut.DeleteFileAsync(
                "linked-outside/target.txt",
                CancellationToken.None);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*workspace*");
            File.Exists(outsideFile).Should().BeTrue();
            (await File.ReadAllTextAsync(outsideFile, CancellationToken.None))
                .Should()
                .Be("outside");
        }
        finally
        {
            DeleteDirectorySymlinkIfExists(linkPath);
            DeleteDirectoryTreeIfExists(outsideRoot);
        }
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ReturnMatchingWorkspaceRelativePaths()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        string programPath = Path.Combine(srcDirectory, "Program.cs");
        await File.WriteAllTextAsync(programPath, "class Program {}", CancellationToken.None);

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false),
            CancellationToken.None);

        result.Matches.Should().Equal("src/Program.cs");
    }

    [Fact]
    public async Task SearchTextAsync_Should_ReturnMatchingWorkspaceRelativePaths()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        string programPath = Path.Combine(srcDirectory, "Program.cs");
        await File.WriteAllTextAsync(programPath, "class Program {}", CancellationToken.None);

        WorkspaceTextSearchResult result = await sut.SearchTextAsync(
            new WorkspaceTextSearchRequest("Program", "src", CaseSensitive: false),
            CancellationToken.None);

        result.Matches.Should().ContainSingle();
        result.Matches[0].Path.Should().Be("src/Program.cs");
        result.Matches[0].LineNumber.Should().Be(1);
        result.Matches[0].LineText.Should().Contain("Program");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_UpdateAndAddFiles()
    {
        WorkspaceFileService sut = CreateSut();
        string existingFile = Path.Combine(_workspaceRoot, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(existingFile)!);
        await File.WriteAllTextAsync(
            existingFile,
            "class Program\n{\n    // TODO\n}\n",
            CancellationToken.None);

        WorkspaceApplyPatchResult result = await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: src/Program.cs
            @@
             class Program
             {
            -    // TODO
            +    // done
             }
            *** Add File: src/Notes.txt
            +remember the tests
            *** End Patch
            """,
            CancellationToken.None);

        result.FileCount.Should().Be(2);
        result.Files.Select(static file => file.Path).Should().BeEquivalentTo(["src/Program.cs", "src/Notes.txt"]);
        (await File.ReadAllTextAsync(existingFile, CancellationToken.None)).Should().Contain("// done");
        (await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "src", "Notes.txt"), CancellationToken.None))
            .Should().Be("remember the tests");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_DenySymlinkDirectoryBreakout()
    {
        WorkspaceFileService sut = CreateSut();
        string outsideRoot = CreateOutsideDirectory();
        string outsideFile = Path.Combine(outsideRoot, "target.txt");
        string linkPath = Path.Combine(_workspaceRoot, "linked-outside");
        await File.WriteAllTextAsync(outsideFile, "outside\n", CancellationToken.None);

        try
        {
            if (!TryCreateDirectorySymlink(linkPath, outsideRoot))
            {
                return;
            }

            Func<Task> act = () => sut.ApplyPatchAsync(
                """
                *** Begin Patch
                *** Update File: linked-outside/target.txt
                @@
                -outside
                +changed
                *** End Patch
                """,
                CancellationToken.None);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*workspace*");
            (await File.ReadAllTextAsync(outsideFile, CancellationToken.None))
                .Should()
                .Be("outside\n");
        }
        finally
        {
            DeleteDirectorySymlinkIfExists(linkPath);
            DeleteDirectoryTreeIfExists(outsideRoot);
        }
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AddFinalNewline_When_RemovedLineHadNoNewlineMarker()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "settings.json");
        await File.WriteAllTextAsync(filePath, "{}", CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: settings.json
            @@
            -{}
            \ No newline at end of file
            +{}
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("{}\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_RemoveFinalNewline_When_AddedLineHasNoNewlineMarker()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "settings.json");
        await File.WriteAllTextAsync(filePath, "{}\n", CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: settings.json
            @@
            -{}
            +{}
            \ No newline at end of file
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("{}");
    }

    [Fact]
    public async Task WriteFileWithTrackingAsync_Should_ReturnUndoableBeforeAndAfterStates()
    {
        WorkspaceFileService sut = CreateSut();

        WorkspaceFileWriteExecutionResult result = await sut.WriteFileWithTrackingAsync(
            "README.md",
            "hello",
            overwrite: true,
            CancellationToken.None);

        result.EditTransaction.BeforeStates.Should().ContainSingle();
        result.EditTransaction.BeforeStates[0].Path.Should().Be("README.md");
        result.EditTransaction.BeforeStates[0].Exists.Should().BeFalse();
        result.EditTransaction.AfterStates.Should().ContainSingle();
        result.EditTransaction.AfterStates[0].Path.Should().Be("README.md");
        result.EditTransaction.AfterStates[0].Exists.Should().BeTrue();
        result.EditTransaction.AfterStates[0].Content.Should().Be("hello");
    }

    [Fact]
    public async Task DeleteFileWithTrackingAsync_Should_ReturnUndoableBeforeAndAfterStates()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "README.md");
        await File.WriteAllTextAsync(filePath, "hello", CancellationToken.None);

        WorkspaceFileDeleteExecutionResult result = await sut.DeleteFileWithTrackingAsync(
            "README.md",
            CancellationToken.None);

        result.EditTransaction.BeforeStates.Should().ContainSingle();
        result.EditTransaction.BeforeStates[0].Path.Should().Be("README.md");
        result.EditTransaction.BeforeStates[0].Exists.Should().BeTrue();
        result.EditTransaction.BeforeStates[0].Content.Should().Be("hello");
        result.EditTransaction.AfterStates.Should().ContainSingle();
        result.EditTransaction.AfterStates[0].Path.Should().Be("README.md");
        result.EditTransaction.AfterStates[0].Exists.Should().BeFalse();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyFileEditStatesAsync_Should_RestoreFilesFromTrackedStates()
    {
        WorkspaceFileService sut = CreateSut();
        string readmePath = Path.Combine(_workspaceRoot, "README.md");
        await File.WriteAllTextAsync(readmePath, "changed", CancellationToken.None);

        await sut.ApplyFileEditStatesAsync(
            [
                new WorkspaceFileEditState("README.md", exists: true, content: "original"),
                new WorkspaceFileEditState("docs/notes.txt", exists: false, content: null)
            ],
            CancellationToken.None);

        (await File.ReadAllTextAsync(readmePath, CancellationToken.None)).Should().Be("original");
        File.Exists(Path.Combine(_workspaceRoot, "docs", "notes.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ListDirectoryAsync_Should_ExcludeNanoIgnoredPaths()
    {
        await WriteNanoIgnoreAsync(
            """
            *.secret
            ignored/
            [Bb]in/
            !keep.secret
            """);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "public.txt"), "visible", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "token.secret"), "hidden", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "keep.secret"), "visible", CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "ignored", "note.txt"), "hidden", CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "bin"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "bin", "app.dll"), "hidden", CancellationToken.None);

        WorkspaceFileService sut = CreateSut();

        WorkspaceDirectoryListResult result = await sut.ListDirectoryAsync(
            ".",
            recursive: true,
            CancellationToken.None);

        result.Entries.Select(static entry => entry.Path)
            .Should()
            .BeEquivalentTo([".nanoagent", ".nanoagent/.nanoignore", "keep.secret", "public.txt"]);
    }

    [Fact]
    public async Task ReadFileAsync_Should_DenyNanoIgnoredPath()
    {
        await WriteNanoIgnoreAsync("*.secret");
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "token.secret"), "hidden", CancellationToken.None);
        WorkspaceFileService sut = CreateSut();

        Func<Task> act = () => sut.ReadFileAsync(
            "token.secret",
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*excluded by .nanoagent/.nanoignore*");
    }

    [Fact]
    public async Task SearchTextAsync_Should_ExcludeNanoIgnoredFiles()
    {
        await WriteNanoIgnoreAsync(
            """
            secrets/
            *.log
            !visible.log
            """);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "README.md"), "needle", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "app.log"), "needle", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "visible.log"), "needle", CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "secrets"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "secrets", "token.txt"), "needle", CancellationToken.None);

        WorkspaceFileService sut = CreateSut();

        WorkspaceTextSearchResult result = await sut.SearchTextAsync(
            new WorkspaceTextSearchRequest("needle", ".", CaseSensitive: false),
            CancellationToken.None);

        result.Matches.Select(static match => match.Path)
            .Should()
            .BeEquivalentTo(["README.md", "visible.log"]);
    }

    [Fact]
    public async Task WriteFileAsync_Should_DenyNanoIgnoredPath()
    {
        await WriteNanoIgnoreAsync("secrets/");
        WorkspaceFileService sut = CreateSut();

        Func<Task> act = () => sut.WriteFileAsync(
            "secrets/token.txt",
            "hidden",
            overwrite: true,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*excluded by .nanoagent/.nanoignore*");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_DenyNanoIgnoredPath()
    {
        await WriteNanoIgnoreAsync("*.secret");
        WorkspaceFileService sut = CreateSut();

        Func<Task> act = () => sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Add File: token.secret
            +hidden
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*excluded by .nanoagent/.nanoignore*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            DeleteDirectoryTreeIfExists(_workspaceRoot);
        }
    }

    private WorkspaceFileService CreateSut()
    {
        return new WorkspaceFileService(new StubWorkspaceRootProvider(_workspaceRoot));
    }

    private async Task WriteNanoIgnoreAsync(string content)
    {
        string nanoAgentDirectory = Path.Combine(_workspaceRoot, ".nanoagent");
        Directory.CreateDirectory(nanoAgentDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(nanoAgentDirectory, ".nanoignore"),
            content,
            CancellationToken.None);
    }

    private static string CreateOutsideDirectory()
    {
        string outsideRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Outside-{Guid.NewGuid():N}");

        Directory.CreateDirectory(outsideRoot);
        return outsideRoot;
    }

    private static bool TryCreateDirectorySymlink(
        string linkPath,
        string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (IsSymlinkCreationUnavailable(exception))
        {
            return false;
        }
    }

    private static bool IsSymlinkCreationUnavailable(Exception exception)
    {
        return exception is UnauthorizedAccessException or PlatformNotSupportedException ||
            OperatingSystem.IsWindows() && exception is IOException;
    }

    private static void DeleteDirectorySymlinkIfExists(string linkPath)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(linkPath);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                Directory.Delete(linkPath);
                return;
            }

            File.Delete(linkPath);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void DeleteDirectoryTreeIfExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(entry);
                }
                else
                {
                    DeleteDirectoryTreeIfExists(entry);
                }

                continue;
            }

            File.Delete(entry);
        }

        Directory.Delete(directoryPath);
    }

    private sealed class StubWorkspaceRootProvider : IWorkspaceRootProvider
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
}
