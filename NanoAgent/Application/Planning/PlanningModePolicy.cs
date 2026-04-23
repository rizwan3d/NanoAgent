using System.Text.RegularExpressions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Application.Planning;

internal static class PlanningModePolicy
{
    private static readonly string[] ExecutionApprovalSignals =
    [
        "continue",
        "continue with the plan",
        "go ahead",
        "go ahead with the plan",
        "proceed",
        "proceed with the plan",
        "execute",
        "execute the plan",
        "implement it",
        "implement the plan",
        "apply it",
        "apply the plan",
        "run it",
        "run the plan",
        "approved",
        "approve",
        "yes"
    ];

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

    private static readonly HashSet<string> SafeEnvironmentProbeCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "bun",
        "cargo",
        "clang",
        "clang++",
        "cmake",
        "composer",
        "csc",
        "deno",
        "dotnet",
        "gcc",
        "g++",
        "Get-Command",
        "go",
        "gradle",
        "java",
        "javac",
        "kotlinc",
        "make",
        "msbuild",
        "mvn",
        "ninja",
        "node",
        "npm",
        "npx",
        "php",
        "pip",
        "pip3",
        "pnpm",
        "poetry",
        "powershell",
        "pwsh",
        "py",
        "pytest",
        "python",
        "python3",
        "ruby",
        "rustc",
        "ruff",
        "sh",
        "swift",
        "tsc",
        "uv",
        "uvx",
        "where",
        "which",
        "yarn"
    };

    private static readonly HashSet<string> SafeEnvironmentProbeArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "--help",
        "--info",
        "--list-runtimes",
        "--list-sdks",
        "--version",
        "-?",
        "-h",
        "-version",
        "/?",
        "/version",
        "help",
        "version"
    };

    private const string ApprovedExecutionInstructions =
        """
        APPROVED EXECUTION PHASE IS ACTIVE.
        The user approved a previously saved plan for this section.
        Use the saved plan below as the baseline task list, but refine it if repo evidence requires a safer or smaller implementation.
        Work through the task list one task at a time and keep the current active step explicit.
        Finish the requested work when practical instead of returning another plan.
        """;

    private const string ExecutionPlanInstruction =
        """
        Execution plan for the current request:
        - Use the approved plan below as the task list.
        - Execute the work one task at a time using available tools when needed.
        - Keep the immediate next step explicit while you work.
        - Keep the visible plan synchronized with update_plan when the task spans multiple meaningful steps.
        - When scaffold tooling is part of the plan, use fully specified, non-interactive commands that include the destination name, template or preset, and confirmation flags up front instead of waiting for interactive prompts.
        - If repo evidence shows the approved plan is incomplete, unsafe, or too large, revise it deliberately instead of following it blindly.
        - Finish or deliberately revise the current task before moving to the next one.
        - Finish the requested work when practical.
        - In your final response, include:
          Objective
          Task list or completed steps
          Validation
          Risks / unknowns
        """;

    private const string ToolDrivenPlanningInstructions =
        """
        Tool-driven planning:
        - `planning_mode` is available as an optional tool when you want to inspect and think first.
        - Use planning_mode for ambiguous, risky, multi-step, or unfamiliar work when the right implementation path is not yet clear.
        - `update_plan` is available for live planning. Use it for meaningful multi-step work after you have enough evidence to define concrete steps.
        - An update_plan call must contain a concise ordered task list. Use statuses `completed`, `in_progress`, and `pending`; keep at most one step `in_progress`; keep completed steps first, then the active step, then pending steps.
        - Before planning, gather repo evidence with read-only tools instead of guessing.
        - When relevant, use `shell_command` to inspect the environment and check installed build tools, SDKs, compilers, package managers, or runtimes with safe probe commands such as `dotnet --info`, `python --version`, `node --version`, `gcc --version`, `where.exe dotnet`, or `Get-Command cmake`.
        - When the correct usage of a build tool, framework, library, SDK, or API is unfamiliar or likely to have changed, use `web_run` to check the current official documentation or domain references before you rely on it.
        - During execution, use `shell_command` for real toolchain work when it materially advances the task: scaffold projects, restore or install dependencies, run code generation, build, test, lint, format, or inspect runtime behavior.
        - For project scaffolding commands such as `npm create vite@latest`, include the project name, template or preset, and any supported confirmation flags in the initial command so the scaffold stays non-interactive.
        - Prefer repo-native validation commands such as `dotnet build`, `dotnet test`, `npm test`, `npm run build`, `python -m pytest`, `cargo test`, `go test ./...`, `mvn test`, or `gradle test` when those toolchains are present.
        - When you produce a plan, prefer sections such as: Objective, Verified facts, Assumptions / open questions, Relevant files / areas, Environment / toolchain, Candidate approaches, Recommended approach, Immediate next step, Plan, Validation, Risks / unknowns.
        - A plan should:
          - restate the objective clearly
          - start from verified repo evidence, not guesses
          - separate verified facts from assumptions or open questions
          - identify the relevant files, modules, commands, toolchains, or subsystems
          - briefly compare approaches when there is a meaningful choice and recommend one
          - keep the immediate next step explicit
          - give a high-quality ordered task list with the immediate next step first
          - include concrete validation commands and key risks
          - recommend the best path when multiple approaches exist
        - Follow the `planning_mode` tool output closely when it is available: use its guidance and suggested sections as the plan-writing contract for the current task.
        - Avoid low-quality plans such as: "look at the code", "make the change", "test it". Those are too vague unless you ground them in actual files, commands, and risks.
        - After planning, execute the resulting task list one task at a time instead of jumping across unfinished work.
        - Keep the live plan synchronized as work progresses: mark finished steps completed, keep the current step in_progress, and revise the list when evidence changes the safest path.
        - If the user asked only for a plan, respond with the plan and stop.
        - Otherwise, after planning, continue execution in the same turn when practical.
        - Do not wait for explicit plan approval unless the user asked you to stop after planning.
        """;

    public static string? CreateToolDrivenConversationSystemPrompt(string? basePrompt)
    {
        return AppendInstructions(basePrompt, ToolDrivenPlanningInstructions);
    }

    public static string? CreateExecutionSystemPrompt(
        string? basePrompt,
        string planningSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planningSummary);

        string instructions =
            $"{ApprovedExecutionInstructions}{Environment.NewLine}{Environment.NewLine}{ExecutionPlanInstruction}{Environment.NewLine}{Environment.NewLine}{planningSummary.Trim()}";

        return AppendInstructions(basePrompt, instructions);
    }

    public static bool IsExecutionApproval(string? userInput)
    {
        return MatchesIntent(userInput, ExecutionApprovalSignals);
    }

    public static bool ShouldBypassShellAllowlistForPlanning(string commandText)
    {
        return TryGetPlanningShellCommandInfo(
            commandText,
            out string[] tokens,
            out string commandName) &&
               IsSafeEnvironmentProbeCommand(tokens, commandName);
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
            ToolArguments.TryGetNonEmptyString(
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

    public static PermissionEvaluationResult? EvaluateProfileRestrictions(
        ToolPermissionPolicy permissionPolicy,
        ToolExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(permissionPolicy);
        ArgumentNullException.ThrowIfNull(context);

        AgentProfilePermissionOverlay overlay = context.Session.AgentProfile.PermissionIntent;

        if (overlay.EditMode == AgentProfileEditMode.ReadOnly &&
            IsWriteLikeTool(permissionPolicy))
        {
            return PermissionEvaluationResult.Denied(
                "profile_readonly_write_blocked",
                $"Agent profile '{context.Session.AgentProfile.Name}' is read-only and cannot modify files. Tool '{context.ToolName}' is only available in an editing profile.",
                PermissionMode.Deny,
                CreateRequest(context));
        }

        if (overlay.ShellMode == AgentProfileShellMode.SafeInspectionOnly &&
            permissionPolicy.Shell is not null &&
            ToolArguments.TryGetNonEmptyString(
                context.Arguments,
                permissionPolicy.Shell.CommandArgumentName,
                out string? commandText) &&
            !IsSafePlanningShellCommand(commandText!, out string denialReason))
        {
            return PermissionEvaluationResult.Denied(
                "profile_shell_blocked",
                $"Agent profile '{context.Session.AgentProfile.Name}' only allows safe inspection shell commands. {denialReason}",
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

    private static bool MatchesIntent(
        string? userInput,
        IReadOnlyList<string> signals)
    {
        string normalizedInput = NormalizeIntentText(userInput);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return false;
        }

        return signals.Any(signal => normalizedInput.Equals(signal, StringComparison.Ordinal) ||
                                     normalizedInput.StartsWith(signal + " ", StringComparison.Ordinal));
    }

    private static string NormalizeIntentText(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return string.Empty;
        }

        return Regex.Replace(
                userInput.Trim().ToLowerInvariant(),
                @"[^a-z0-9]+",
                " ")
            .Trim();
    }

    private static bool IsSafePlanningShellCommand(
        string commandText,
        out string denialReason)
    {
        string normalizedCommand = commandText.Trim();
        if (!TryGetPlanningShellCommandInfo(
                commandText,
                out string[] tokens,
                out string commandName))
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

        if (IsSafeEnvironmentProbeCommand(tokens, commandName))
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

    private static bool TryGetPlanningShellCommandInfo(
        string commandText,
        out string[] tokens,
        out string commandName)
    {
        string normalizedCommand = commandText.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            tokens = [];
            commandName = string.Empty;
            return false;
        }

        if (ShellCommandText.ContainsControlSyntax(normalizedCommand))
        {
            tokens = [];
            commandName = string.Empty;
            return false;
        }

        tokens = ShellCommandText.Tokenize(normalizedCommand);
        if (tokens.Length == 0)
        {
            commandName = string.Empty;
            return false;
        }

        commandName = ShellCommandText.NormalizeCommandToken(tokens[0]);
        return !string.IsNullOrWhiteSpace(commandName);
    }

    private static bool IsSafeEnvironmentProbeCommand(
        IReadOnlyList<string> tokens,
        string commandName)
    {
        if (!SafeEnvironmentProbeCommands.Contains(commandName))
        {
            return false;
        }

        if (string.Equals(commandName, "where", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "which", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "Get-Command", StringComparison.OrdinalIgnoreCase))
        {
            return tokens.Count == 2 && IsSimpleCommandSubject(tokens[1]);
        }

        return tokens.Count == 2 &&
               SafeEnvironmentProbeArguments.Contains(tokens[1]);
    }

    private static bool IsSimpleCommandSubject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"^[A-Za-z0-9._+\-]+$",
            RegexOptions.CultureInvariant);
    }

}
