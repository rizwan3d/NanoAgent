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

        result.Matches.Select(static match => match.Path).Should().Equal("src/Program.cs");
        result.Matches[0].MatchKind.Should().Be("filename_contains");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ApplyGlobFilter()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.txt"), "class Program {}", CancellationToken.None);

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false, Glob: "**/*.cs"),
            CancellationToken.None);

        result.Matches.Select(static match => match.Path).Should().Equal("src/Program.cs");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ApplyFuzzyMatching()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "WorkspaceFileService.cs"), "class WorkspaceFileService {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.cs"), "class Program {}", CancellationToken.None);

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("wkspfilesrv", "src", CaseSensitive: false, Fuzzy: true),
            CancellationToken.None);

        result.Matches.Should().ContainSingle();
        result.Matches[0].Path.Should().Be("src/WorkspaceFileService.cs");
        result.Matches[0].MatchKind.Should().Be("filename_subsequence");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_RespectRequestedLimit()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.Tests.cs"), "class ProgramTests {}", CancellationToken.None);

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false, Limit: 1),
            CancellationToken.None);

        result.Matches.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ExcludeRootGitIgnoredFiles()
    {
        await WriteGitIgnoreAsync(
            """
            ignored/
            """);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "src", "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "ignored", "Program.cs"), "class Program {}", CancellationToken.None);

        WorkspaceFileService sut = CreateSut();

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", ".", CaseSensitive: false),
            CancellationToken.None);

        result.Matches.Select(static match => match.Path).Should().Equal("src/Program.cs");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_RespectNestedGitIgnoreRules()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src", "visible"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src", "generated"));
        await WriteGitIgnoreAsync(
            """
            generated/
            *.snap
            !keep.snap
            """,
            "src");
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "src", "visible", "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "src", "generated", "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "src", "test.snap"), "snapshot", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "src", "keep.snap"), "snapshot", CancellationToken.None);

        WorkspaceFileService sut = CreateSut();

        WorkspaceFileSearchResult programResult = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false),
            CancellationToken.None);
        WorkspaceFileSearchResult snapResult = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("snap", "src", CaseSensitive: false),
            CancellationToken.None);

        programResult.Matches.Select(static match => match.Path).Should().Equal("src/visible/Program.cs");
        snapResult.Matches.Select(static match => match.Path).Should().Equal("src/keep.snap");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_DenyGitIgnoredSearchPath()
    {
        await WriteGitIgnoreAsync("ignored/");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "ignored", "Program.cs"), "class Program {}", CancellationToken.None);
        WorkspaceFileService sut = CreateSut();

        Func<Task> act = () => sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "ignored", CaseSensitive: false),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*excluded by .gitignore*");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ApplyRegexWholeWordMatching()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "SearchFilesTool.cs"), "class SearchFilesTool {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "SearchFilesToolbar.cs"), "class SearchFilesToolbar {}", CancellationToken.None);

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest(
                "SearchFilesTool",
                "src",
                CaseSensitive: false,
                Mode: WorkspaceFileSearchModes.Regex,
                Regex: true,
                WholeWord: true),
            CancellationToken.None);

        result.Matches.Select(static match => match.Path).Should().Equal("src/SearchFilesTool.cs");
        result.Matches[0].MatchKind.Should().Be("filename_regex");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ApplyIncludeAndExcludeGlobs()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        string objDirectory = Path.Combine(srcDirectory, "obj");
        Directory.CreateDirectory(objDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(objDirectory, "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.txt"), "class Program {}", CancellationToken.None);

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest(
                "Program",
                "src",
                CaseSensitive: false,
                IncludeGlobs: ["**/*.cs"],
                ExcludeGlobs: ["**/obj/**"]),
            CancellationToken.None);

        result.Matches.Select(static match => match.Path).Should().Equal("src/Program.cs");
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ReturnPagedMatchesAndNextCursor()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(srcDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.A.cs"), "class ProgramA {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.B.cs"), "class ProgramB {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.C.cs"), "class ProgramC {}", CancellationToken.None);

        WorkspaceFileSearchResult firstPage = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false, Limit: 2),
            CancellationToken.None);

        firstPage.Matches.Should().HaveCount(2);
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        firstPage.TotalMatchCount.Should().Be(3);

        WorkspaceFileSearchResult secondPage = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest(
                "Program",
                "src",
                CaseSensitive: false,
                Limit: 2,
                Cursor: firstPage.NextCursor),
            CancellationToken.None);

        secondPage.Matches.Should().ContainSingle();
        secondPage.HasMore.Should().BeFalse();
        secondPage.Offset.Should().Be(2);
    }

    [Fact]
    public async Task SearchFilesAsync_Should_ExcludeGeneratedDirectories_UnlessRequested()
    {
        WorkspaceFileService sut = CreateSut();
        string srcDirectory = Path.Combine(_workspaceRoot, "src");
        string generatedDirectory = Path.Combine(srcDirectory, "obj");
        Directory.CreateDirectory(generatedDirectory);
        await File.WriteAllTextAsync(Path.Combine(srcDirectory, "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(generatedDirectory, "GeneratedProgram.cs"), "class GeneratedProgram {}", CancellationToken.None);

        WorkspaceFileSearchResult defaultResult = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false),
            CancellationToken.None);
        WorkspaceFileSearchResult includeGeneratedResult = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", "src", CaseSensitive: false, IncludeGenerated: true),
            CancellationToken.None);

        defaultResult.Matches.Select(static match => match.Path).Should().Equal("src/Program.cs");
        includeGeneratedResult.Matches.Select(static match => match.Path)
            .Should()
            .Contain(["src/Program.cs", "src/obj/GeneratedProgram.cs"]);
    }

    [Fact]
    public async Task SearchFilesAsync_Should_IncludeIgnoredFiles_When_Requested()
    {
        await WriteGitIgnoreAsync("ignored/");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "src", "Program.cs"), "class Program {}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "ignored", "Program.cs"), "class Program {}", CancellationToken.None);
        WorkspaceFileService sut = CreateSut();

        WorkspaceFileSearchResult result = await sut.SearchFilesAsync(
            new WorkspaceFileSearchRequest("Program", ".", CaseSensitive: false, IncludeIgnored: true),
            CancellationToken.None);

        result.Matches.Select(static match => match.Path)
            .Should()
            .Contain(["src/Program.cs", "ignored/Program.cs"]);
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
            .Should().Be("remember the tests\n");
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
    public async Task ApplyPatchAsync_Should_RejectNoNewlineMarker_BeforeLaterContextInSameHunk()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(filePath, "a\nb\n", CancellationToken.None);

        Func<Task> act = () => sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: notes.txt
            @@
            -a
            +A
            \ No newline at end of file
             b
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<FormatException>()
            .WithMessage("*final resulting line*");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_RejectNoNewlineMarker_InNonFinalHunk()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(filePath, "a\nb\nc\n", CancellationToken.None);

        Func<Task> act = () => sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: notes.txt
            @@
            -a
            +A
            \ No newline at end of file
            @@
            -c
            +C
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<FormatException>()
            .WithMessage("*final resulting line*");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_RejectEmptyPatch()
    {
        WorkspaceFileService sut = CreateSut();

        Func<Task> act = () => sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<FormatException>()
            .WithMessage("*at least one*");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_UseUpdateHunkContextLabel()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "script.py");
        await File.WriteAllTextAsync(
            filePath,
            "def other():\n    print(\"Hi\")\n\ndef greet():\n    print(\"Hi\")\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: script.py
            @@ def greet():
            -    print("Hi")
            +    print("Hello")
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("def other():\n    print(\"Hi\")\n\ndef greet():\n    print(\"Hello\")\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_InsertRelativeToAnchoredInsertionOnlyHunk()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "app.txt");
        await File.WriteAllTextAsync(
            filePath,
            "before\ntarget line\nafter\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: app.txt
            @@ target line
            +inserted near target
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("before\ntarget line\ninserted near target\nafter\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AcceptRepeatedHunkLabel_AsImplicitContextLine()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "script.py");
        await File.WriteAllTextAsync(
            filePath,
            "def greet():\n    print(\"Hi\")\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: script.py
            @@ def greet():
            def greet():
            -    print("Hi")
            +    print("Hello")
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("def greet():\n    print(\"Hello\")\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_UseEndOfFileAnchor()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(
            filePath,
            "target\nmiddle\ntarget\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: notes.txt
            @@
            -target
            +tail
            *** End of File
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("target\nmiddle\ntail\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AddFinalNewline_ForUpdatesByDefault()
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
            +{"enabled":true}
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("{\"enabled\":true}\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AllowBlankSeparatorLine_BeforeFirstHunk()
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
            +{"enabled":true}
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("{\"enabled\":true}\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AllowBlankSeparatorLine_BeforeEndPatch()
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
            +{"enabled":true}

            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("{\"enabled\":true}\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_MatchEscapedGreaterThanInCSharpLambdaContext()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "AppDbContext.cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            builder.Entity<User>()
                .Property(entity => entity.Name)
                .HasMaxLength(200);
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: AppDbContext.cs
            @@ builder.Entity<User>()
            -    .Property(entity =\u003E entity.Name)
            +    .Property(entity => entity.DisplayName)
                 .HasMaxLength(200);
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be(
                """
                builder.Entity<User>()
                    .Property(entity => entity.DisplayName)
                    .HasMaxLength(200);
                """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_MatchEscapedGreaterThanInGenericAndComparisonContext()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "Rules.cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            if (items.Count > 0)
            {
                Dictionary<string, List<int>> cache = [];
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: Rules.cs
            @@
            -if (items.Count \u003E 0)
            +if (items.Count > 1)
             {
            -    Dictionary<string, List<int\u003E\u003E cache = [];
            +    Dictionary<string, List<long>> cache = [];
             }
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be(
                """
                if (items.Count > 1)
                {
                    Dictionary<string, List<long>> cache = [];
                }
                """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_NotBroadlyDecodeUnrelatedUnicodeEscapes()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "Text.cs");
        await File.WriteAllTextAsync(
            filePath,
            "const string Message = \"snowman: ☃\";\n",
            CancellationToken.None);

        Func<Task> act = () => sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: Text.cs
            @@
            -const string Message = "snowman: \u2603";
            +const string Message = "winter";
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*target context*");

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("const string Message = \"snowman: ☃\";\n");
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
    public async Task ApplyFileEditStatesAsync_Should_RespectPlatformPathComparison_ForCaseOnlyPaths()
    {
        WorkspaceFileService sut = CreateSut();

        await sut.ApplyFileEditStatesAsync(
            [
                new WorkspaceFileEditState("Foo.txt", exists: true, content: "upper"),
                new WorkspaceFileEditState("foo.txt", exists: true, content: "lower")
            ],
            CancellationToken.None);

        string upperPath = Path.Combine(_workspaceRoot, "Foo.txt");
        string lowerPath = Path.Combine(_workspaceRoot, "foo.txt");

        if (OperatingSystem.IsWindows())
        {
            File.Exists(upperPath).Should().BeTrue();
            (await File.ReadAllTextAsync(upperPath, CancellationToken.None)).Should().Be("lower");
        }
        else
        {
            File.Exists(upperPath).Should().BeTrue();
            File.Exists(lowerPath).Should().BeTrue();
            (await File.ReadAllTextAsync(upperPath, CancellationToken.None)).Should().Be("upper");
            (await File.ReadAllTextAsync(lowerPath, CancellationToken.None)).Should().Be("lower");
        }
    }

    [Fact]
    public async Task ApplyPatchWithTrackingAsync_Should_RespectPlatformPathComparison_ForCaseOnlyRename()
    {
        WorkspaceFileService sut = CreateSut();
        string sourcePath = Path.Combine(_workspaceRoot, "Foo.txt");
        await File.WriteAllTextAsync(sourcePath, "hello", CancellationToken.None);

        WorkspaceApplyPatchExecutionResult result = await sut.ApplyPatchWithTrackingAsync(
            """
            *** Begin Patch
            *** Update File: Foo.txt
            *** Move to: foo.txt
            *** End Patch
            """,
            CancellationToken.None);

        result.EditTransaction.Should().NotBeNull();

        if (OperatingSystem.IsWindows())
        {
            result.EditTransaction!.BeforeStates
                .Should()
                .ContainSingle()
                .Which.Path
                .Should()
                .Be("Foo.txt");
            result.EditTransaction.AfterStates
                .Should()
                .ContainSingle()
                .Which.Path
                .Should()
                .Be("Foo.txt");
        }
        else
        {
            result.EditTransaction!.BeforeStates.Select(static state => state.Path).Should().Equal("Foo.txt", "foo.txt");
            result.EditTransaction.AfterStates.Select(static state => state.Path).Should().Equal("Foo.txt", "foo.txt");
        }
    }

    [Fact]
    public async Task ApplyPatchWithTrackingAsync_Should_RollBackEarlierOperations_WhenALaterOperationFails()
    {
        WorkspaceFileService sut = CreateSut();
        string createdPath = Path.Combine(_workspaceRoot, "created.txt");

        Func<Task> act = () => sut.ApplyPatchWithTrackingAsync(
            """
            *** Begin Patch
            *** Add File: created.txt
            +this file is created
            *** Update File: missing.txt
            @@
            -old
            +new
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("File 'missing.txt' does not exist.");
        File.Exists(createdPath).Should().BeFalse();
        File.Exists(Path.Combine(_workspaceRoot, "missing.txt")).Should().BeFalse();
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

    [Fact]
    public async Task ApplyPatchAsync_Should_AcceptCrlfLineEndings_InPatchText()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(
            filePath,
            "line1\nline2\nline3\n",
            CancellationToken.None);

        // Patch uses CRLF line endings
        WorkspaceApplyPatchResult result = await sut.ApplyPatchAsync(
            "*** Begin Patch\r\n*** Update File: src/Program.cs\r\n@@\r\n line1\r\n line2\r\n-line3\r\n+changed\r\n*** End Patch\r\n",
            CancellationToken.None);

        result.FileCount.Should().Be(1);
        result.AddedLineCount.Should().Be(1);
        result.RemovedLineCount.Should().Be(1);
        string actualContent = await File.ReadAllTextAsync(filePath, CancellationToken.None);
        actualContent.Should().Be("line1\nline2\nchanged\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AcceptMixedLineEndings_InPatchText()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(
            filePath,
            "keep\nremove\n",
            CancellationToken.None);

        // Header uses CRLF, hunk body uses LF
        WorkspaceApplyPatchResult result = await sut.ApplyPatchAsync(
            "*** Begin Patch\r\n*** Update File: notes.txt\r\n@@\r\n-remove\r\n+added\r\n*** End Patch\r\n",
            CancellationToken.None);

        result.FileCount.Should().Be(1);
        string actualContent = await File.ReadAllTextAsync(filePath, CancellationToken.None);
        actualContent.Should().Be("keep\nadded\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_StripMarkdownCodeFences()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(
            filePath,
            "before\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            ```patch
            *** Begin Patch
            *** Update File: notes.txt
            @@
            -before
            +after
            *** End Patch
            ```
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("after\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AcceptUnifiedDiffHeaders()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "config.ini");
        await File.WriteAllTextAsync(
            filePath,
            "setting1=old\nsetting2=value\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            --- a/config.ini
            +++ b/config.ini
            @@ -1,2 +1,2 @@
            -setting1=old
            +setting1=new
             setting2=value
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("setting1=new\nsetting2=value\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_InsertMissingHunkHeader_BeforeDiffLines()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "sample.txt");
        await File.WriteAllTextAsync(
            filePath,
            "old\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: sample.txt
            -old
            +new
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("new\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AutoPrefixBlankLine_InsideUpdateHunk()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "sample.cs");

        await File.WriteAllTextAsync(
            filePath,
            "line1\n\nline3\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: sample.cs
            @@
             line1

            -line3
            +line4
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("line1\n\nline4\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AutoPrefixBlankLine_InsideAddFile()
    {
        WorkspaceFileService sut = CreateSut();

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Add File: notes.txt
            +first

            +third
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "notes.txt"), CancellationToken.None))
            .Should()
            .Be("first\n\nthird\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_NotIncludeBlankSeparator_BetweenAddFileAndNextOperation()
    {
        WorkspaceFileService sut = CreateSut();
        string existingPath = Path.Combine(_workspaceRoot, "existing.txt");
        await File.WriteAllTextAsync(existingPath, "old\n", CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Add File: hello.txt
            +Hello world

            *** Update File: existing.txt
            @@
            -old
            +new
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(_workspaceRoot, "hello.txt"), CancellationToken.None))
            .Should()
            .Be("Hello world\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_PreserveExactContent_ForMoveOnlyUpdate()
    {
        WorkspaceFileService sut = CreateSut();
        string sourcePath = Path.Combine(_workspaceRoot, "old.txt");
        string destinationPath = Path.Combine(_workspaceRoot, "new.txt");
        await File.WriteAllTextAsync(
            sourcePath,
            "hello\r\nthere",
            CancellationToken.None);

        WorkspaceApplyPatchResult result = await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: old.txt
            *** Move to: new.txt
            *** End Patch
            """,
            CancellationToken.None);

        result.FileCount.Should().Be(1);
        File.Exists(sourcePath).Should().BeFalse();
        string actualContent = await File.ReadAllTextAsync(destinationPath, CancellationToken.None);
        actualContent.Should().Be("hello\r\nthere");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_HandleCrlfLineEndings_InExistingFile()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "config.ini");
        // Existing file has CRLF line endings
        await File.WriteAllTextAsync(
            filePath,
            "setting1=old\r\nsetting2=value\r\n",
            CancellationToken.None);

        WorkspaceApplyPatchResult result = await sut.ApplyPatchAsync(
            "*** Begin Patch\n*** Update File: config.ini\n@@\n-setting1=old\n+setting1=new\n*** End Patch\n",
            CancellationToken.None);

        result.FileCount.Should().Be(1);
        string actualContent = await File.ReadAllTextAsync(filePath, CancellationToken.None);
        actualContent.Should().Be("setting1=new\r\nsetting2=value\r\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_AddFile_WithCrlfPatchText()
    {
        WorkspaceFileService sut = CreateSut();

        WorkspaceApplyPatchResult result = await sut.ApplyPatchAsync(
            "*** Begin Patch\r\n*** Add File: readme.txt\r\n+hello world\r\n+second line\r\n*** End Patch\r\n",
            CancellationToken.None);

        result.FileCount.Should().Be(1);
        result.AddedLineCount.Should().Be(2);
        string actualContent = await File.ReadAllTextAsync(
            Path.Combine(_workspaceRoot, "readme.txt"),
            CancellationToken.None);
        actualContent.Should().Be("hello world\nsecond line\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_RetrySingleFileMatch_UsingSmallerChangedWindow()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "retry.txt");
        await File.WriteAllTextAsync(
            filePath,
            "header changed\nold value\nfooter changed\n",
            CancellationToken.None);

        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: retry.txt
            @@
             header
            -old value
            +new value
             footer
            *** End Patch
            """,
            CancellationToken.None);

        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("header changed\nnew value\nfooter changed\n");
    }

    [Fact]
    public async Task ApplyPatchAsync_Should_RejectAmbiguousRepeatedMatch()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "ambiguous.txt");
        await File.WriteAllTextAsync(
            filePath,
            "dup\nkeep\n\ndup\nkeep\n",
            CancellationToken.None);

        Func<Task> act = () => sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: ambiguous.txt
            @@
            -dup
            +changed
            *** End Patch
            """,
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*matched multiple locations*Candidate starting lines: 1, 4*");
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

    private async Task WriteGitIgnoreAsync(
        string content,
        string? relativeDirectory = null)
    {
        string directory = string.IsNullOrWhiteSpace(relativeDirectory)
            ? _workspaceRoot
            : Path.Combine(_workspaceRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, ".gitignore"),
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

    [Fact]
    public async Task ApplyPatchAsync_Should_AcceptSpacePrefixedBlankContextLine()
    {
        WorkspaceFileService sut = CreateSut();
        string filePath = Path.Combine(_workspaceRoot, "sample.cs");
    
        await File.WriteAllTextAsync(
            filePath,
            "line1\n\nline3\n",
            CancellationToken.None);
    
        await sut.ApplyPatchAsync(
            """
            *** Begin Patch
            *** Update File: sample.cs
            @@
             line1
             
            -line3
            +line4
            *** End Patch
            """,
            CancellationToken.None);
    
        (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Should()
            .Be("line1\n\nline4\n");
    }
}
