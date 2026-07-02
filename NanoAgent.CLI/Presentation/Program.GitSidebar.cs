using NanoAgent.Application.Models;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Diagnostics;

namespace NanoAgent.CLI;

public enum GitSidebarLineKind
{
    Text,
    Branch,
    StagedFile,
    ChangedFile
}

// One rendered row of the git sidebar. File rows are clickable and keyboard actionable.
public sealed record GitSidebarLine(
    string Markup,
    string Plain,
    string? FilePath,
    GitSidebarLineKind Kind = GitSidebarLineKind.Text,
    string? RelativePath = null,
    string? StatusCode = null);

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

        width = Math.Clamp(windowWidth / 5, 15, 30);
        return true;
    }

    private static void ToggleGitSidebar(AppState state)
    {
        state.IsGitSidebarVisible = !state.IsGitSidebarVisible;
        if (state.IsGitSidebarVisible)
        {
            InvalidateGitSidebar(state);
        }
    }

    private static void InvalidateGitSidebar(AppState state)
    {
        state.GitSidebarCache = null;
        state.GitSidebarCacheTime = DateTimeOffset.MinValue;
    }

    private static IRenderable BuildGitSidebarPanel(AppState state, int windowHeight)
    {
        IReadOnlyList<GitSidebarLine> lines = GetGitSidebarLines(state);
        int viewportHeight = Math.Max(1, windowHeight - 2);

        int maxScroll = Math.Max(0, lines.Count - viewportHeight);
        state.GitSidebarScrollOffset = Math.Clamp(state.GitSidebarScrollOffset, 0, maxScroll);
        state.GitSidebarTotalLineCount = lines.Count;
        state.GitSidebarViewportHeight = viewportHeight;

        EnsureGitSidebarSelection(state, lines);

        int start = state.GitSidebarScrollOffset;
        List<string> markup = [];
        GitSidebarLine[] visibleLines = new GitSidebarLine[viewportHeight];
        for (int index = 0; index < viewportHeight; index++)
        {
            int sourceIndex = start + index;
            if (sourceIndex < lines.Count)
            {
                GitSidebarLine line = lines[sourceIndex];
                markup.Add(sourceIndex == state.GitSidebarSelectedIndex
                    ? HighlightGitSidebarMarkup(line)
                    : line.Markup);
                visibleLines[index] = line;
            }
            else
            {
                markup.Add(string.Empty);
                visibleLines[index] = new GitSidebarLine(string.Empty, string.Empty, null);
            }
        }

        state.VisibleGitSidebarLines = visibleLines;

        string scrollHint = lines.Count > viewportHeight
            ? $" [grey]{start + 1}-{Math.Min(lines.Count, start + viewportHeight)}/{lines.Count}[/]"
            : string.Empty;

        return new Panel(new Markup(string.Join('\n', markup)))
            .Header(SafeHeaderMarkup($"[bold]Git[/] [grey](F7)[/]{scrollHint}"))
            .Border(BoxBorder.Square)
            .Expand();
    }

    private static string HighlightGitSidebarMarkup(GitSidebarLine line)
    {
        string text = string.IsNullOrEmpty(line.Plain) ? " " : line.Plain;
        return $"[black on grey70]{Markup.Escape(text)}[/]";
    }

    private static void ScrollGitSidebar(AppState state, int delta)
    {
        int maxScroll = Math.Max(0, state.GitSidebarTotalLineCount - state.GitSidebarViewportHeight);
        state.GitSidebarScrollOffset = Math.Clamp(state.GitSidebarScrollOffset + delta, 0, maxScroll);
    }

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

    private static bool TryHandleGitSidebarKey(AppState state, ConsoleKeyInfo key)
    {
        if (!state.IsGitSidebarVisible || state.ActiveModal is not null || state.IsReaderViewActive || state.IsCopyModeActive)
        {
            return false;
        }

        IReadOnlyList<GitSidebarLine> lines = GetGitSidebarLines(state);
        EnsureGitSidebarSelection(state, lines);

        int pageSize = Math.Max(1, state.GitSidebarViewportHeight - 1);
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                MoveGitSidebarSelection(state, lines, -1);
                return true;
            case ConsoleKey.DownArrow:
                MoveGitSidebarSelection(state, lines, 1);
                return true;
            case ConsoleKey.Home:
                MoveGitSidebarSelectionToEdge(state, lines, forward: true);
                return true;
            case ConsoleKey.End:
                MoveGitSidebarSelectionToEdge(state, lines, forward: false);
                return true;
            case ConsoleKey.PageUp:
                MoveGitSidebarSelectionByPage(state, lines, -pageSize);
                return true;
            case ConsoleKey.PageDown:
                MoveGitSidebarSelectionByPage(state, lines, pageSize);
                return true;
            case ConsoleKey.Enter:
                ActivateGitSidebarSelection(state);
                return true;
            case ConsoleKey.S when key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                ToggleGitSidebarStageSelection(state);
                return true;
            case ConsoleKey.O when key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                RunGitPushFromSidebar(state);
                return true;
            case ConsoleKey.P when key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                RunGitPullFromSidebar(state);
                return true;
            case ConsoleKey.D when key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                PromptDiscardGitSidebarSelection(state);
                return true;
            case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                PromptCommitFromGitSidebar(state);
                return true;
            case ConsoleKey.B when key.Modifiers.HasFlag(ConsoleModifiers.Alt):
                PromptBranchActionFromGitSidebar(state);
                return true;
            default:
                return false;
        }
    }

    private static IReadOnlyList<GitSidebarLine> GetGitSidebarLines(AppState state)
    {
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

        lines.Add(new GitSidebarLine(
            SectionSidebarMarkup($"branch {branch ?? "(detached)"}", contentWidth),
            TruncateFromRight($"branch {branch ?? "(detached)"}", contentWidth),
            null,
            GitSidebarLineKind.Branch));

        AddQueuedPromptLines(lines, state, contentWidth);

        lines.Add(new GitSidebarLine(string.Empty, string.Empty, null));
        lines.Add(SectionSidebarLine("Recent commits", contentWidth));
        AddCommitLines(lines, root, contentWidth);

        (List<(string Code, string Rel)> staged, List<(string Code, string Rel)> changes) = ReadGitStatus(root);
        AddFileLines(lines, "Staged", staged, repoRoot, contentWidth, GitSidebarLineKind.StagedFile);
        AddFileLines(lines, "Changes", changes, repoRoot, contentWidth, GitSidebarLineKind.ChangedFile);

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
                $" [aqua]*[/] [grey]{Markup.Escape(text)}[/]",
                $" * {text}",
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
        int contentWidth,
        GitSidebarLineKind kind)
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
            string displayName = BuildGitSidebarFileDisplayText(rel);
            string nameTrunc = TruncateFromRight(displayName, Math.Max(0, contentWidth - 3));
            string plain = $" {code} {nameTrunc}";
            string markup = $" [{color}]{Markup.Escape(code)}[/] [underline]{Markup.Escape(nameTrunc)}[/]";
            string full = Path.GetFullPath(Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            lines.Add(new GitSidebarLine(markup, plain, full, kind, rel, code));
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

    private static string SectionSidebarMarkup(string text, int contentWidth)
    {
        string trunc = TruncateFromRight(text, contentWidth);
        return $"[bold]{Markup.Escape(trunc)}[/]";
    }

    private static GitSidebarLine PlainSidebarLine(string text, int contentWidth, string color)
    {
        string trunc = TruncateFromRight(text, contentWidth);
        return new GitSidebarLine($"[{color}]{Markup.Escape(trunc)}[/]", trunc, null);
    }

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

    private static string BuildGitSidebarFileDisplayText(string relativePath)
    {
        string normalizedPath = relativePath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrEmpty(fileName) ||
            string.Equals(fileName, normalizedPath, StringComparison.Ordinal))
        {
            return normalizedPath;
        }

        return $"{fileName} ({normalizedPath})";
    }

    private static void EnsureGitSidebarSelection(AppState state, IReadOnlyList<GitSidebarLine> lines)
    {
        if (lines.Count == 0)
        {
            state.GitSidebarSelectedIndex = -1;
            return;
        }

        if (IsGitSidebarActionable(lines, state.GitSidebarSelectedIndex))
        {
            EnsureGitSidebarSelectionVisible(state);
            return;
        }

        state.GitSidebarSelectedIndex = FindNextGitSidebarActionableIndex(lines, 0, forward: true);
        EnsureGitSidebarSelectionVisible(state);
    }

    private static bool IsGitSidebarActionable(IReadOnlyList<GitSidebarLine> lines, int index)
    {
        return index >= 0 &&
            index < lines.Count &&
            lines[index].Kind is GitSidebarLineKind.Branch or GitSidebarLineKind.StagedFile or GitSidebarLineKind.ChangedFile;
    }

    private static int FindNextGitSidebarActionableIndex(
        IReadOnlyList<GitSidebarLine> lines,
        int startIndex,
        bool forward)
    {
        if (lines.Count == 0)
        {
            return -1;
        }

        int index = Math.Clamp(startIndex, 0, lines.Count - 1);
        int step = forward ? 1 : -1;

        while (index >= 0 && index < lines.Count)
        {
            if (IsGitSidebarActionable(lines, index))
            {
                return index;
            }

            index += step;
        }

        return -1;
    }

    private static void MoveGitSidebarSelection(AppState state, IReadOnlyList<GitSidebarLine> lines, int direction)
    {
        if (lines.Count == 0)
        {
            return;
        }

        int current = Math.Clamp(state.GitSidebarSelectedIndex, 0, lines.Count - 1);
        int next = FindNextGitSidebarActionableIndex(lines, current + direction, direction > 0);
        if (next >= 0)
        {
            state.GitSidebarSelectedIndex = next;
            EnsureGitSidebarSelectionVisible(state);
        }
    }

    private static void MoveGitSidebarSelectionToEdge(AppState state, IReadOnlyList<GitSidebarLine> lines, bool forward)
    {
        int next = FindNextGitSidebarActionableIndex(lines, forward ? 0 : lines.Count - 1, forward);
        if (next >= 0)
        {
            state.GitSidebarSelectedIndex = next;
            EnsureGitSidebarSelectionVisible(state);
        }
    }

    private static void MoveGitSidebarSelectionByPage(AppState state, IReadOnlyList<GitSidebarLine> lines, int delta)
    {
        if (lines.Count == 0)
        {
            return;
        }

        int current = Math.Clamp(state.GitSidebarSelectedIndex, 0, lines.Count - 1);
        int target = Math.Clamp(current + delta, 0, lines.Count - 1);
        int next = FindNextGitSidebarActionableIndex(lines, target, delta >= 0);
        if (next < 0)
        {
            next = FindNextGitSidebarActionableIndex(lines, target, delta < 0);
        }

        if (next >= 0)
        {
            state.GitSidebarSelectedIndex = next;
            EnsureGitSidebarSelectionVisible(state);
        }
    }

    private static void EnsureGitSidebarSelectionVisible(AppState state)
    {
        if (state.GitSidebarSelectedIndex < 0)
        {
            return;
        }

        if (state.GitSidebarSelectedIndex < state.GitSidebarScrollOffset)
        {
            state.GitSidebarScrollOffset = state.GitSidebarSelectedIndex;
            return;
        }

        int viewportBottom = state.GitSidebarScrollOffset + Math.Max(1, state.GitSidebarViewportHeight) - 1;
        if (state.GitSidebarSelectedIndex > viewportBottom)
        {
            state.GitSidebarScrollOffset = Math.Max(0, state.GitSidebarSelectedIndex - Math.Max(1, state.GitSidebarViewportHeight) + 1);
        }
    }

    private static GitSidebarLine? GetSelectedGitSidebarLine(AppState state)
    {
        IReadOnlyList<GitSidebarLine> lines = GetGitSidebarLines(state);
        EnsureGitSidebarSelection(state, lines);

        return IsGitSidebarActionable(lines, state.GitSidebarSelectedIndex)
            ? lines[state.GitSidebarSelectedIndex]
            : null;
    }

    private static void ActivateGitSidebarSelection(AppState state)
    {
        GitSidebarLine? selected = GetSelectedGitSidebarLine(state);
        if (selected is null)
        {
            return;
        }

        if (selected.Kind == GitSidebarLineKind.Branch)
        {
            PromptBranchActionFromGitSidebar(state);
            return;
        }

        if (selected.FilePath is string path)
        {
            OpenFileInEditor(state, path);
        }
    }

    private static void ToggleGitSidebarStageSelection(AppState state)
    {
        GitSidebarLine? selected = GetSelectedGitSidebarLine(state);
        if (selected is null)
        {
            return;
        }

        if (selected.Kind == GitSidebarLineKind.Branch)
        {
            PromptBranchActionFromGitSidebar(state);
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.RelativePath))
        {
            return;
        }

        bool success = selected.Kind switch
        {
            GitSidebarLineKind.ChangedFile => TryRunGitCommand(
                state,
                $"Staged {selected.RelativePath}.",
                "Failed to stage file",
                "add",
                "--",
                selected.RelativePath),
            GitSidebarLineKind.StagedFile => TryRunGitCommand(
                state,
                $"Unstaged {selected.RelativePath}.",
                "Failed to unstage file",
                "restore",
                "--staged",
                "--",
                selected.RelativePath),
            _ => false
        };

        if (success)
        {
            InvalidateGitSidebar(state);
        }
    }

    private static void PromptDiscardGitSidebarSelection(AppState state)
    {
        GitSidebarLine? selected = GetSelectedGitSidebarLine(state);
        if (selected is null)
        {
            return;
        }

        if (selected.Kind != GitSidebarLineKind.ChangedFile || string.IsNullOrWhiteSpace(selected.RelativePath))
        {
            state.AddSystemMessage("Select a file in Changes to discard its worktree edits.");
            return;
        }

        string relativePath = selected.RelativePath;
        string action = string.Equals(selected.StatusCode, "?", StringComparison.Ordinal)
            ? "Delete untracked file"
            : "Discard file changes";
        string description = string.Equals(selected.StatusCode, "?", StringComparison.Ordinal)
            ? $"Remove {relativePath} from disk. Esc cancels."
            : $"Restore {relativePath} to the version in git. Esc cancels.";

        state.ActiveModal = SelectionModalState<bool>.Create(
            new SelectionPromptRequest<bool>(
                action,
                [
                    new SelectionPromptOption<bool>("Yes", true, "Apply the discard action."),
                    new SelectionPromptOption<bool>("No", false, "Keep the current file changes.")
                ],
                description,
                DefaultIndex: 1,
                AllowCancellation: true),
            new object(),
            onSelected: confirmed =>
            {
                if (!confirmed)
                {
                    state.AddSystemMessage("Discard cancelled.");
                    return;
                }

                bool success = string.Equals(selected.StatusCode, "?", StringComparison.Ordinal)
                    ? TryRunGitCommand(
                        state,
                        $"Deleted untracked file {relativePath}.",
                        "Failed to delete untracked file",
                        "clean",
                        "-f",
                        "--",
                        relativePath)
                    : TryRunGitCommand(
                        state,
                        $"Discarded changes in {relativePath}.",
                        "Failed to discard file changes",
                        "restore",
                        "--",
                        relativePath);

                if (success)
                {
                    InvalidateGitSidebar(state);
                }
            },
            onCancelled: _ => state.AddSystemMessage("Discard cancelled."));
    }

    private static void PromptCommitFromGitSidebar(AppState state)
    {
        string? staged = RunGit(state.RootDirectory, "diff", "--cached", "--name-only");
        if (string.IsNullOrWhiteSpace(staged))
        {
            state.AddSystemMessage("Stage at least one file before committing.");
            return;
        }

        state.ActiveModal = TextModalState.Create(
            new TextPromptRequest(
                "Commit message",
                "Create a git commit from the staged changes. Enter submits, Esc cancels.",
                DefaultValue: null,
                AllowCancellation: true),
            isSecret: false,
            completionToken: new object(),
            onSubmitted: message =>
            {
                string trimmed = message.Trim();
                if (trimmed.Length == 0)
                {
                    state.AddSystemMessage("Commit cancelled: message cannot be empty.");
                    return;
                }

                bool success = TryRunGitCommand(
                    state,
                    $"Committed staged changes: {trimmed}",
                    "Failed to create commit",
                    "commit",
                    "-m",
                    trimmed);

                if (success)
                {
                    InvalidateGitSidebar(state);
                }
            },
            onCancelled: _ => state.AddSystemMessage("Commit cancelled."));
    }

    private static void PromptBranchActionFromGitSidebar(AppState state)
    {
        state.ActiveModal = SelectionModalState<string>.Create(
            new SelectionPromptRequest<string>(
                "Branch actions",
                [
                    new SelectionPromptOption<string>("Switch branch", "switch", "Move this worktree to an existing local branch."),
                    new SelectionPromptOption<string>("Create branch", "create", "Create and switch to a new branch from the current HEAD.")
                ],
                "Pick a branch action. Esc cancels.",
                DefaultIndex: 0,
                AllowCancellation: true),
            new object(),
            onSelected: action =>
            {
                if (string.Equals(action, "switch", StringComparison.Ordinal))
                {
                    PromptSwitchBranchFromGitSidebar(state);
                    return;
                }

                PromptCreateBranchFromGitSidebar(state);
            },
            onCancelled: _ => state.AddSystemMessage("Branch action cancelled."));
    }

    private static void PromptSwitchBranchFromGitSidebar(AppState state)
    {
        string? currentBranch = GetGitBranchName(state.RootDirectory);
        string? output = RunGit(state.RootDirectory, "branch", "--format=%(refname:short)");
        string[] branches = output?
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (branches.Length == 0)
        {
            state.AddSystemMessage("No local branches found.");
            return;
        }

        SelectionPromptOption<string>[] options = branches
            .Select(branch => new SelectionPromptOption<string>(
                branch == currentBranch ? $"Current: {branch}" : branch,
                branch,
                "Switch to this branch."))
            .ToArray();

        int defaultIndex = Array.FindIndex(branches, branch => string.Equals(branch, currentBranch, StringComparison.Ordinal));
        if (defaultIndex < 0)
        {
            defaultIndex = 0;
        }

        state.ActiveModal = SelectionModalState<string>.Create(
            new SelectionPromptRequest<string>(
                "Switch branch",
                options,
                "Choose a local branch. Esc cancels.",
                DefaultIndex: defaultIndex,
                AllowCancellation: true),
            new object(),
            onSelected: branch =>
            {
                bool success = TryRunGitCommand(
                    state,
                    $"Switched to branch {branch}.",
                    "Failed to switch branch",
                    "switch",
                    branch);

                if (success)
                {
                    InvalidateGitSidebar(state);
                }
            },
            onCancelled: _ => state.AddSystemMessage("Branch switch cancelled."));
    }

    private static void PromptCreateBranchFromGitSidebar(AppState state)
    {
        state.ActiveModal = TextModalState.Create(
            new TextPromptRequest(
                "New branch name",
                "Create and switch to a new git branch. Enter submits, Esc cancels.",
                DefaultValue: null,
                AllowCancellation: true),
            isSecret: false,
            completionToken: new object(),
            onSubmitted: branchName =>
            {
                string trimmed = branchName.Trim();
                if (trimmed.Length == 0)
                {
                    state.AddSystemMessage("Branch creation cancelled: name cannot be empty.");
                    return;
                }

                bool success = TryRunGitCommand(
                    state,
                    $"Created and switched to branch {trimmed}.",
                    "Failed to create branch",
                    "switch",
                    "-c",
                    trimmed);

                if (success)
                {
                    InvalidateGitSidebar(state);
                }
            },
            onCancelled: _ => state.AddSystemMessage("Branch creation cancelled."));
    }

    private static void RunGitPushFromSidebar(AppState state)
    {
        if (TryRunGitCommand(
            state,
            "Git push completed.",
            "Failed to push",
            "push"))
        {
            InvalidateGitSidebar(state);
        }
    }

    private static void RunGitPullFromSidebar(AppState state)
    {
        if (TryRunGitCommand(
            state,
            "Git pull completed.",
            "Failed to pull",
            "pull"))
        {
            InvalidateGitSidebar(state);
        }
    }

    private static bool TryRunGitCommand(
        AppState state,
        string successMessage,
        string failurePrefix,
        params string[] arguments)
    {
        (bool succeeded, string? output, string? error) = RunGitWithResult(state.RootDirectory, arguments);
        if (succeeded)
        {
            state.AddSystemMessage(successMessage);
            return true;
        }

        string detail = !string.IsNullOrWhiteSpace(error)
            ? error
            : output ?? "git command failed.";
        state.AddSystemMessage($"{failurePrefix}: {detail}");
        return false;
    }

    private static (bool Succeeded, string? Output, string? Error) RunGitWithResult(
        string workingDirectory,
        params string[] arguments)
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
                return (false, null, "Could not start git.");
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            if (!TryWaitForProcessExit(process, 4000))
            {
                return (false, null, "git timed out.");
            }

            string output = outputTask.GetAwaiter().GetResult().Trim();
            string error = errorTask.GetAwaiter().GetResult().Trim();
            return (process.ExitCode == 0, output, error);
        }
        catch (Exception exception)
        {
            return (false, null, exception.Message);
        }
    }

    private static string? RunGit(string workingDirectory, params string[] arguments)
    {
        (bool succeeded, string? output, _) = RunGitWithResult(workingDirectory, arguments);
        return succeeded ? output : null;
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
        GitSidebarLine[] lines = state.VisibleGitSidebarLines;
        if (index < 0 || index >= lines.Length)
        {
            return;
        }

        GitSidebarLine line = lines[index];
        int selectedIndex = state.GitSidebarScrollOffset + index;
        if (selectedIndex >= 0)
        {
            state.GitSidebarSelectedIndex = selectedIndex;
            EnsureGitSidebarSelectionVisible(state);
        }

        ActivateGitSidebarSelection(state);
    }

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
