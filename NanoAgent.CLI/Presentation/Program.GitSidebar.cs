using Spectre.Console;
using Spectre.Console.Rendering;
using System.Diagnostics;

namespace NanoAgent.CLI;

// One rendered row of the git sidebar. FilePath is set on clickable file rows.
public sealed record GitSidebarLine(string Markup, string Plain, string? FilePath);

public static partial class Program
{
    private const int GitSidebarMinWindowWidth = 30;

    private static bool TryGetGitSidebarWidth(AppState state, int windowWidth, out int width)
    {
        width = 0;
        if (!state.IsGitSidebarVisible || windowWidth < GitSidebarMinWindowWidth)
        {
            return false;
        }

        width = Math.Clamp(windowWidth / 5, 15,30);
        return true;
    }

    private static void ToggleGitSidebar(AppState state)
    {
        state.IsGitSidebarVisible = !state.IsGitSidebarVisible;
        if (state.IsGitSidebarVisible)
        {
            state.GitSidebarCache = null; // refresh on open
        }
    }

    private static IRenderable BuildGitSidebarPanel(AppState state, int windowHeight)
    {
        IReadOnlyList<GitSidebarLine> lines = GetGitSidebarLines(state);
        int viewportHeight = Math.Max(1, windowHeight - 2);

        int maxScroll = Math.Max(0, lines.Count - viewportHeight);
        state.GitSidebarScrollOffset = Math.Clamp(state.GitSidebarScrollOffset, 0, maxScroll);
        state.GitSidebarTotalLineCount = lines.Count;
        state.GitSidebarViewportHeight = viewportHeight;

        int start = state.GitSidebarScrollOffset;
        List<string> markup = [];
        string?[] rowFiles = new string?[viewportHeight];
        for (int index = 0; index < viewportHeight; index++)
        {
            int sourceIndex = start + index;
            if (sourceIndex < lines.Count)
            {
                markup.Add(lines[sourceIndex].Markup);
                rowFiles[index] = lines[sourceIndex].FilePath;
            }
            else
            {
                markup.Add(string.Empty);
            }
        }

        state.VisibleGitSidebarFilePaths = rowFiles;

        string scrollHint = lines.Count > viewportHeight
            ? $" [grey]{start + 1}-{Math.Min(lines.Count, start + viewportHeight)}/{lines.Count}[/]"
            : string.Empty;

        return new Panel(new Markup(string.Join('\n', markup)))
            .Header($"[bold]Git[/] [grey](F7)[/]{scrollHint}")
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static void ScrollGitSidebar(AppState state, int delta)
    {
        int maxScroll = Math.Max(0, state.GitSidebarTotalLineCount - state.GitSidebarViewportHeight);
        state.GitSidebarScrollOffset = Math.Clamp(state.GitSidebarScrollOffset + delta, 0, maxScroll);
    }

    // Ctrl+Up/Down (and Ctrl+PgUp/PgDn) scroll the sidebar on the direct key-read path.
    private static bool TryHandleGitSidebarScrollKey(AppState state, ConsoleKeyInfo key)
    {
        if (!key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return false;
        }

        int page = Math.Max(1, state.GitSidebarViewportHeight - 1);
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                ScrollGitSidebar(state, -MouseWheelScrollLineCount);
                return true;
            case ConsoleKey.DownArrow:
                ScrollGitSidebar(state, MouseWheelScrollLineCount);
                return true;
            case ConsoleKey.PageUp:
                ScrollGitSidebar(state, -page);
                return true;
            case ConsoleKey.PageDown:
                ScrollGitSidebar(state, page);
                return true;
            default:
                return false;
        }
    }

    private static IReadOnlyList<GitSidebarLine> GetGitSidebarLines(AppState state)
    {
        // synchronous git on the render thread, refreshed at most every 2s.
        // Move off-thread if it visibly hitches.
        if (state.GitSidebarCache is { } cached &&
            DateTimeOffset.UtcNow - state.GitSidebarCacheTime < TimeSpan.FromSeconds(2))
        {
            return cached;
        }

        IReadOnlyList<GitSidebarLine> lines = BuildGitSidebarLines(state);
        state.GitSidebarCache = lines;
        state.GitSidebarCacheTime = DateTimeOffset.UtcNow;
        return lines;
    }

    private static IReadOnlyList<GitSidebarLine> BuildGitSidebarLines(AppState state)
    {
        int contentWidth = Math.Max(8, state.GitSidebarWidth - 4);
        string root = state.RootDirectory;
        List<GitSidebarLine> lines = [];

        string? gitDir = FindGitDirectory(root);
        if (gitDir is null)
        {
            lines.Add(PlainSidebarLine("Not a git repository.", contentWidth, "grey"));
            return lines;
        }

        string repoRoot = Directory.GetParent(gitDir.TrimEnd('\\', '/'))?.FullName ?? root;
        string? branch = GetGitBranchName(root);

        lines.Add(SectionSidebarLine($"⎇ {branch ?? "(detached)"}", contentWidth));

        AddQueuedPromptLines(lines, state, contentWidth);

        lines.Add(new GitSidebarLine(string.Empty, string.Empty, null));
        lines.Add(SectionSidebarLine("Recent commits", contentWidth));
        AddCommitLines(lines, root, contentWidth);

        (List<(string Code, string Rel)> staged, List<(string Code, string Rel)> changes) = ReadGitStatus(root);
        AddFileLines(lines, "Staged", staged, repoRoot, contentWidth);
        AddFileLines(lines, "Changes", changes, repoRoot, contentWidth);

        return lines;
    }

    private static void AddQueuedPromptLines(List<GitSidebarLine> lines, AppState state, int contentWidth)
    {
        if (state.PendingSubmissions.Count == 0)
        {
            return;
        }

        lines.Add(new GitSidebarLine(string.Empty, string.Empty, null));
        lines.Add(SectionSidebarLine($"Queued ({state.PendingSubmissions.Count})", contentWidth));

        foreach (PendingSubmission submission in state.PendingSubmissions)
        {
            string text = TruncateFromRight(DescribePendingSubmission(submission), Math.Max(0, contentWidth - 2));
            lines.Add(new GitSidebarLine(
                $" [aqua]•[/] [grey]{Markup.Escape(text)}[/]",
                $" • {text}",
                null));
        }
    }

    private static void AddCommitLines(List<GitSidebarLine> lines, string root, int contentWidth)
    {
        string? log = RunGit(root, "log", "-10", "--pretty=format:%h%x09%s");
        if (string.IsNullOrEmpty(log))
        {
            lines.Add(PlainSidebarLine("  (none)", contentWidth, "grey"));
            return;
        }

        foreach (string raw in log.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            int tab = raw.IndexOf('\t');
            string hash = tab >= 0 ? raw[..tab] : raw;
            string message = tab >= 0 ? raw[(tab + 1)..] : string.Empty;
            string messageTrunc = TruncateFromRight(message, Math.Max(0, contentWidth - hash.Length - 2));
            string plain = $" {hash} {messageTrunc}";
            string markup = $" [yellow]{Markup.Escape(hash)}[/] [grey]{Markup.Escape(messageTrunc)}[/]";
            lines.Add(new GitSidebarLine(markup, plain, null));
        }
    }

    private static void AddFileLines(
        List<GitSidebarLine> lines,
        string title,
        List<(string Code, string Rel)> items,
        string repoRoot,
        int contentWidth)
    {
        lines.Add(new GitSidebarLine(string.Empty, string.Empty, null));
        lines.Add(SectionSidebarLine($"{title} ({items.Count})", contentWidth));

        if (items.Count == 0)
        {
            lines.Add(PlainSidebarLine("  (none)", contentWidth, "grey"));
            return;
        }

        foreach ((string code, string rel) in items)
        {
            string color = code switch
            {
                "?" => "grey",
                "D" => "red",
                "A" => "green",
                "M" => "yellow",
                _ => "aqua"
            };
            string nameTrunc = TruncateFromRight(rel, Math.Max(0, contentWidth - 3));
            string plain = $" {code} {nameTrunc}";
            string markup = $" [{color}]{Markup.Escape(code)}[/] [underline]{Markup.Escape(nameTrunc)}[/]";
            string full = Path.GetFullPath(Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            lines.Add(new GitSidebarLine(markup, plain, full));
        }
    }

    private static (List<(string Code, string Rel)> Staged, List<(string Code, string Rel)> Changes) ReadGitStatus(string root)
    {
        List<(string, string)> staged = [];
        List<(string, string)> changes = [];

        string? status = RunGit(root, "status", "--porcelain");
        if (string.IsNullOrEmpty(status))
        {
            return (staged, changes);
        }

        foreach (string line in status.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length < 4)
            {
                continue;
            }

            char index = line[0];
            char worktree = line[1];
            string path = line[3..];

            int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                path = path[(arrow + 4)..];
            }

            path = UnquoteGitPath(path);

            if (index != ' ' && index != '?')
            {
                staged.Add((index.ToString(), path));
            }

            if (worktree == '?')
            {
                changes.Add(("?", path));
            }
            else if (worktree != ' ')
            {
                changes.Add((worktree.ToString(), path));
            }
        }

        return (staged, changes);
    }

    private static GitSidebarLine SectionSidebarLine(string text, int contentWidth)
    {
        string trunc = TruncateFromRight(text, contentWidth);
        return new GitSidebarLine($"[bold]{Markup.Escape(trunc)}[/]", trunc, null);
    }

    private static GitSidebarLine PlainSidebarLine(string text, int contentWidth, string color)
    {
        string trunc = TruncateFromRight(text, contentWidth);
        return new GitSidebarLine($"[{color}]{Markup.Escape(trunc)}[/]", trunc, null);
    }

    // strips surrounding quotes only; octal/unicode escapes left as-is.
    private static string UnquoteGitPath(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
        {
            return path[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return path;
    }

    private static string? RunGit(string workingDirectory, params string[] arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!TryWaitForProcessExit(process, 2000))
            {
                return null;
            }

            string output = outputTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static void HandleGitSidebarClick(AppState state, int row)
    {
        if (state.ActiveModal is not null ||
            state.IsReaderViewActive ||
            state.IsCopyModeActive)
        {
            return;
        }

        int index = row - state.GitSidebarContentTopRow;
        string?[] files = state.VisibleGitSidebarFilePaths;
        if (index < 0 || index >= files.Length)
        {
            return;
        }

        if (files[index] is string path)
        {
            OpenFileInEditor(state, path);
        }
    }

    // Opens a file in VS Code, falling back to the OS default editor for its type.
    private static void OpenFileInEditor(AppState state, string filePath)
    {
        if (!File.Exists(filePath))
        {
            state.AddSystemMessage($"File not found: {filePath}");
            return;
        }

        string quoted = $"\"{filePath}\"";

        if (OperatingSystem.IsWindows())
        {
            // ShellExecute resolves code.cmd via PATH and throws when VS Code is absent,
            // so the fallback to the OS default editor still fires.
            if (TryStartProcess("code", quoted, useShell: true) ||
                TryStartProcess(filePath, string.Empty, useShell: true))
            {
                return;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (TryStartProcess("code", quoted, useShell: false) ||
                TryStartProcess("open", quoted, useShell: false))
            {
                return;
            }
        }
        else
        {
            if (TryStartProcess("code", quoted, useShell: false) ||
                TryStartProcess("xdg-open", quoted, useShell: false))
            {
                return;
            }
        }

        state.AddSystemMessage($"Could not open {filePath}.");
    }

    private static bool TryStartProcess(string fileName, string arguments, bool useShell)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = useShell,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}
