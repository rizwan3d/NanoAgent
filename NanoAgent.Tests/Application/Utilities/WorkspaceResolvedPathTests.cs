using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using System.Diagnostics;

namespace NanoAgent.Tests.Application.Utilities;

public sealed class WorkspaceResolvedPathTests : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _outsideRoot;

    public WorkspaceResolvedPathTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"NanoAgent-Resolved-{Guid.NewGuid():N}");
        _outsideRoot = Path.Combine(Path.GetTempPath(), $"NanoAgent-Resolved-Outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_outsideRoot);
    }

    [Fact]
    public void Resolve_ShouldRejectNestedSymlinkBreakout_ForWrites()
    {
        string nestedDirectory = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(nestedDirectory);
        string linkPath = Path.Combine(nestedDirectory, "outside-link");

        if (!TryCreateDirectorySymlink(linkPath, _outsideRoot))
        {
            return;
        }

        Action act = () => WorkspaceResolvedPath.Resolve(
            _workspaceRoot,
            Path.Combine("src", "outside-link", "config.json"),
            ToolPathAccessKind.Write);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*workspace*");
    }

    [Fact]
    public void Resolve_ShouldRejectBrokenSymlink()
    {
        string linkPath = Path.Combine(_workspaceRoot, "broken-link");
        string missingTarget = Path.Combine(_outsideRoot, "missing-target");

        if (!TryCreateDirectorySymlink(linkPath, missingTarget))
        {
            return;
        }

        Action act = () => WorkspaceResolvedPath.Resolve(
            _workspaceRoot,
            Path.Combine("broken-link", "child.txt"),
            ToolPathAccessKind.Read);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*workspace*");
    }

    [Fact]
    public void Resolve_ShouldRejectWindowsJunctionBreakout_ForReads()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string junctionPath = Path.Combine(_workspaceRoot, "junction-outside");
        if (!TryCreateDirectoryJunction(junctionPath, _outsideRoot))
        {
            return;
        }

        Action act = () => WorkspaceResolvedPath.Resolve(
            _workspaceRoot,
            Path.Combine("junction-outside", "target.txt"),
            ToolPathAccessKind.Read);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*within the current workspace*");
    }

    public void Dispose()
    {
        DeleteDirectoryTreeIfExists(_workspaceRoot);
        DeleteDirectoryTreeIfExists(_outsideRoot);
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
        catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException ||
                                         OperatingSystem.IsWindows() && exception is IOException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectoryJunction(
        string linkPath,
        string targetPath)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
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
}
