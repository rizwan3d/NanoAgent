using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Tools;
using FluentAssertions;

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
        WorkspaceFileService sut = new(new StubWorkspaceRootProvider(_workspaceRoot));

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
        WorkspaceFileService sut = new(new StubWorkspaceRootProvider(_workspaceRoot));
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

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
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
