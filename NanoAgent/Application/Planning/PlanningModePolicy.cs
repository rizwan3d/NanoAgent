using System.Text.Json;
using System.Text.RegularExpressions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Application.Planning;

internal static class PlanningModePolicy
{
    private static readonly HashSet<string> VisibleToolNames = new(StringComparer.Ordinal)
    {
        AgentToolNames.DirectoryList,
        AgentToolNames.FileRead,
        AgentToolNames.SearchFiles,
        AgentToolNames.TextSearch,
        AgentToolNames.ShellCommand,
        AgentToolNames.WebSearch
    };

    private static readonly HashSet<string> SafeInspectionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat",
        "dir",
        "find",
        "findstr",
        "Get-ChildItem",
        "Get-Content",
        "Get-Item",
        "Get-Location",
        "grep",
        "head",
        "ls",
        "pwd",
        "rg",
        "Select-String",
        "type",
        "which"
    };

    private static readonly HashSet<string> SafeGitSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "branch",
        "diff",
        "log",
        "ls-files",
        "remote",
        "rev-parse",
        "show",
        "status"
    };

    private const string PlanningInstructions =
        """
        You are NanoAgent in Planning Mode.

        Your role is to think before acting. You are not here to implement immediately. You are here to understand the task, inspect the codebase, identify the right approach, and produce a high-quality execution plan.

        Operating mode:
        - Analyze first, act second.
        - Do not write files.
        - Do not apply patches.
        - Do not make destructive changes.
        - Do not pretend work is done unless it has actually been done.
        - Prefer reading, searching, listing, and reasoning.

        Primary goals:
        1. Understand the user's objective precisely.
        2. Inspect the relevant parts of the workspace before proposing changes.
        3. Identify the minimum set of files, modules, commands, and risks involved.
        4. Produce a clear, practical, step-by-step plan that an execution agent could follow.
        5. Explicitly state uncertainties, assumptions, and blockers.
        6. Stop short of implementation unless the user explicitly promotes the plan.

        Behavior rules:
        - Be skeptical of first impressions.
        - Ground your plan in actual repo structure, not guesses.
        - If code inspection is needed, inspect before planning.
        - Prefer the smallest viable solution that satisfies the task.
        - Distinguish facts from assumptions.
        - If there are multiple valid approaches, compare them briefly and recommend one.
        - Call out dangerous or high-impact steps.
        - Flag missing information instead of inventing it.
        - Keep plans actionable, not vague.

        Tool usage rules:
        - Allowed: read/search/list/inspect/status-type actions.
        - Allowed: safe diagnostic shell commands for inspection.
        - Not allowed: file writes, patching, destructive shell actions, or irreversible changes.
        - If implementation would be required to continue, say so clearly and ask for promotion.

        When responding to coding tasks, structure your answer like this:

        Objective
        - Restate the task in one or two sentences.

        Current understanding
        - What the code or repo appears to do.
        - What parts are most likely relevant.
        - What you verified versus what is still inferred.

        Relevant files / areas
        - List the files, modules, classes, commands, or subsystems likely involved.

        Plan
        1. Step one...
        2. Step two...
        3. Step three...

        Risks / unknowns
        - Edge cases
        - Architectural risks
        - Missing context
        - Things that should be verified during execution

        Recommended approach
        - State the best path forward and why.

        Promotion note
        - State clearly that no edits will be made in Planning Mode.
        - Tell the user that the plan can be promoted for implementation.

        Quality bar:
        - Plans should be specific enough that execution can begin with minimal ambiguity.
        - Avoid generic advice.
        - Avoid bloated plans with unnecessary steps.
        - Prefer correctness, clarity, and realism over sounding impressive.

        If the task is ambiguous, do the best possible inspection-based planning with the available context, then clearly mark the open questions.
        """;

    private const string ExecutionInstructions =
        """
        AUTOMATIC EXECUTION PHASE IS ACTIVE.
        The planning phase for the current request has completed and the plan is now promoted for implementation.
        Execute the task list step by step using the available tools when needed.
        Make the smallest effective changes, validate when practical, and finish the user's requested work instead of stopping at analysis.
        In the final response, include a concise task list or execution summary plus any remaining risks or verification gaps.
        """;

    private const string ExecutionPlanInstruction =
        """
        Execution plan for the current request:
        - Use the approved plan below as the task list.
        - Execute the work one step at a time using available tools when needed.
        - Finish the requested work when practical.
        - In your final response, include:
          Objective
          Task list or completed steps
          Validation
          Risks / unknowns
        """;

    public static IReadOnlyList<ToolDefinition> FilterPlanningTools(
        IReadOnlyList<ToolDefinition> toolDefinitions)
    {
        ArgumentNullException.ThrowIfNull(toolDefinitions);

        return toolDefinitions
            .Where(definition => VisibleToolNames.Contains(definition.Name))
            .ToArray();
    }

    public static IReadOnlySet<string> GetPlanningToolNames()
    {
        return new HashSet<string>(VisibleToolNames, StringComparer.Ordinal);
    }

    public static string? CreatePlanningSystemPrompt(string? basePrompt)
    {
        return AppendInstructions(basePrompt, PlanningInstructions);
    }

    public static string? CreateExecutionSystemPrompt(
        string? basePrompt,
        string? planningSummary)
    {
        string instructions = string.IsNullOrWhiteSpace(planningSummary)
            ? ExecutionInstructions
            : $"{ExecutionInstructions}{Environment.NewLine}{Environment.NewLine}{ExecutionPlanInstruction}{Environment.NewLine}{Environment.NewLine}{planningSummary.Trim()}";

        return AppendInstructions(basePrompt, instructions);
    }

    public static IReadOnlyList<string> ExtractPlanTasks(string planningSummary)
    {
        if (string.IsNullOrWhiteSpace(planningSummary))
        {
            return [];
        }

        string[] lines = planningSummary
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);

        int planHeadingIndex = Array.FindIndex(
            lines,
            static line => string.Equals(line.Trim(), "Plan", StringComparison.OrdinalIgnoreCase));

        List<string> tasks = [];
        if (planHeadingIndex >= 0)
        {
            for (int index = planHeadingIndex + 1; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (tasks.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (TryExtractStep(line, out string? task))
                {
                    tasks.Add(task);
                    continue;
                }

                if (tasks.Count > 0)
                {
                    break;
                }
            }
        }

        if (tasks.Count > 0)
        {
            return tasks;
        }

        foreach (string rawLine in lines)
        {
            if (TryExtractStep(rawLine.Trim(), out string? task))
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    public static PermissionEvaluationResult? EvaluateRestrictions(
        ToolPermissionPolicy permissionPolicy,
        ToolExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(permissionPolicy);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExecutionPhase != ConversationExecutionPhase.Planning)
        {
            return null;
        }

        if (IsWriteLikeTool(permissionPolicy))
        {
            return PermissionEvaluationResult.Denied(
                "planning_phase_write_blocked",
                $"The automatic planning phase cannot modify files. Tool '{context.ToolName}' is only allowed during execution.",
                PermissionMode.Deny,
                CreateRequest(context));
        }

        if (permissionPolicy.Shell is not null &&
            TryGetOptionalString(
                context.Arguments,
                permissionPolicy.Shell.CommandArgumentName,
                out string? commandText) &&
            !IsSafePlanningShellCommand(commandText!, out string denialReason))
        {
            return PermissionEvaluationResult.Denied(
                "planning_phase_shell_blocked",
                denialReason,
                PermissionMode.Deny,
                CreateRequest(context, [commandText!]));
        }

        return null;
    }

    private static string? AppendInstructions(
        string? basePrompt,
        string instructions)
    {
        string? normalizedBasePrompt = string.IsNullOrWhiteSpace(basePrompt)
            ? null
            : basePrompt.Trim();

        if (normalizedBasePrompt is null)
        {
            return instructions;
        }

        return $"{normalizedBasePrompt}{Environment.NewLine}{Environment.NewLine}{instructions}";
    }

    private static bool IsWriteLikeTool(ToolPermissionPolicy permissionPolicy)
    {
        if (permissionPolicy.Patch is not null)
        {
            return true;
        }

        if ((permissionPolicy.ToolTags ?? [])
            .Any(static tag => string.Equals(tag, "edit", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return (permissionPolicy.FilePaths ?? [])
            .Any(static rule => rule.Kind == ToolPathAccessKind.Write);
    }

    private static PermissionRequestDescriptor CreateRequest(
        ToolExecutionContext context,
        IReadOnlyList<string>? subjects = null)
    {
        return new PermissionRequestDescriptor(
            context.ToolName,
            context.ToolName,
            [context.ToolName],
            subjects ?? []);
    }

    private static bool TryGetOptionalString(
        JsonElement arguments,
        string propertyName,
        out string? value)
    {
        if (arguments.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString()?.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }

    private static bool TryExtractStep(
        string line,
        out string task)
    {
        Match match = Regex.Match(
            line,
            @"^\s*(?:\d+[\.\)]|[-*])\s+(?<task>.+?)\s*$");

        if (!match.Success)
        {
            task = string.Empty;
            return false;
        }

        task = match.Groups["task"].Value.Trim();
        return task.Length > 0;
    }

    private static bool IsSafePlanningShellCommand(
        string commandText,
        out string denialReason)
    {
        string normalizedCommand = commandText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            denialReason =
                "The automatic planning phase only allows non-empty inspection commands.";
            return false;
        }

        if (ContainsShellControlSyntax(normalizedCommand))
        {
            denialReason =
                $"The automatic planning phase only allows simple inspection commands. '{normalizedCommand}' is execution-only.";
            return false;
        }

        string[] tokens = Tokenize(normalizedCommand);
        if (tokens.Length == 0)
        {
            denialReason =
                "The automatic planning phase only allows valid inspection commands.";
            return false;
        }

        string commandName = NormalizeCommandToken(tokens[0]);
        if (string.IsNullOrWhiteSpace(commandName))
        {
            denialReason =
                "The automatic planning phase only allows valid inspection commands.";
            return false;
        }

        if (SafeInspectionCommands.Contains(commandName))
        {
            denialReason = string.Empty;
            return true;
        }

        if (string.Equals(commandName, "git", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length >= 2 && SafeGitSubcommands.Contains(tokens[1]))
            {
                denialReason = string.Empty;
                return true;
            }

            denialReason =
                $"The automatic planning phase only allows read-only git inspection commands. '{normalizedCommand}' is execution-only.";
            return false;
        }

        denialReason =
            $"The automatic planning phase only allows safe inspection commands. '{normalizedCommand}' is execution-only.";
        return false;
    }

    private static bool ContainsShellControlSyntax(string commandText)
    {
        return commandText.Contains('|') ||
               commandText.Contains(';') ||
               commandText.Contains("&&", StringComparison.Ordinal) ||
               commandText.Contains("||", StringComparison.Ordinal) ||
               commandText.Contains('>') ||
               commandText.Contains('<') ||
               commandText.Contains("$(", StringComparison.Ordinal) ||
               commandText.Contains('`');
    }

    private static string[] Tokenize(string commandText)
    {
        MatchCollection matches = Regex.Matches(
            commandText,
            "\"[^\"]*\"|'[^']*'|\\S+");

        if (matches.Count == 0)
        {
            return [];
        }

        List<string> tokens = new(matches.Count);
        foreach (Match match in matches)
        {
            string value = match.Value.Trim();
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                tokens.Add(value);
            }
        }

        return tokens.ToArray();
    }

    private static string NormalizeCommandToken(string token)
    {
        string trimmedToken = token.Trim();
        if (string.IsNullOrWhiteSpace(trimmedToken))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileName(trimmedToken.Replace('/', Path.DirectorySeparatorChar));
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
