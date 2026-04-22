using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class PlanningModeTool : ITool
{
    private static readonly string[] Instructions =
    [
        "Inspect the relevant codebase and facts before editing, and ground the plan in actual repo evidence instead of guesses.",
        "Check installed build tools, compilers, SDKs, package managers, or runtimes with safe shell probes before choosing scaffold, build, or test commands.",
        "Name the likely files, modules, commands, toolchains, constraints, and validation paths that matter for this task.",
        "Separate verified facts from assumptions or open questions, and call out what still needs confirmation.",
        "When multiple reasonable approaches exist, compare them briefly and recommend one.",
        "Keep the immediate next step explicit and make the first task the first thing you would really do.",
        "Use shell_command for toolchain work during execution when it materially advances the task: project scaffolding, dependency restore/install, code generation, build, test, lint, or format checks.",
        "Use update_plan for non-trivial execution work: publish concise steps, keep exactly one active in_progress step, and update statuses as work advances.",
        "Produce a high-quality ordered task list in Codex style that names likely files, commands, validation steps, and risks.",
        "Avoid vague plans like 'look at the code', 'make the change', or 'test it' unless you ground them in concrete files, commands, and checks.",
        "Keep one active step at a time and revise the plan when new evidence changes the safest path; if the user asked only for a plan, stop after planning, otherwise continue execution when practical."
    ];

    private static readonly string[] SuggestedResponseSections =
    [
        "Objective",
        "Verified facts",
        "Assumptions / open questions",
        "Environment / toolchain",
        "Relevant files / areas",
        "Candidate approaches",
        "Recommended approach",
        "Immediate next step",
        "Plan",
        "Validation",
        "Risks / unknowns"
    ];

    private static readonly string[] QualityChecklist =
    [
        "Start from verified repo evidence and name what you inspected.",
        "Mention the likely files, systems, commands, or toolchains when they matter.",
        "Keep the immediate next step explicit and put it first in the task list.",
        "Include concrete validation commands or checks.",
        "Call out important risks, regressions, or open questions.",
        "Recommend one approach when there is a real tradeoff.",
        "Do not produce a vague plan."
    ];

    public string Description =>
        "Switch into a Codex-style plan-first workflow for the current task. Use this when you want to inspect the repo, check the local toolchain when relevant, separate verified facts from assumptions, compare approaches, think through risks, and produce a high-quality task list before making changes. This tool does not modify files. After planning, call update_plan for meaningful multi-step execution, then continue in the same turn and work one step at a time unless the user asked only for a plan.";

    public string Name => AgentToolNames.PlanningMode;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "bypassUserPermissionRules": true
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "objective": {
              "type": "string",
              "description": "The task or goal that should be planned before execution."
            }
          },
          "required": ["objective"],
          "additionalProperties": false
        }
        """;

    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "objective", out string? objective))
        {
            return Task.FromResult(ToolResultFactory.InvalidArguments(
                "missing_objective",
                "Tool 'planning_mode' requires a non-empty 'objective' string.",
                new ToolRenderPayload(
                    "Invalid planning_mode arguments",
                    "Provide a non-empty 'objective' string.")));
        }

        PlanningModeResult result = new(
            objective!,
            Instructions,
            SuggestedResponseSections);

        return Task.FromResult(ToolResultFactory.Success(
            $"Planning mode activated for '{objective}'.",
            result,
            ToolJsonContext.Default.PlanningModeResult,
            new ToolRenderPayload(
                $"Planning mode: {objective}",
                BuildRenderText(objective!))));
    }

    private static string BuildRenderText(string objective)
    {
        List<string> lines =
        [
            $"Objective: {objective}",
            string.Empty,
            "Planning guidance:"
        ];

        lines.AddRange(Instructions.Select(static (item, index) => $"{index + 1}. {item}"));
        lines.Add(string.Empty);
        lines.Add("Suggested sections:");
        lines.AddRange(SuggestedResponseSections.Select(static section => $"- {section}"));
        lines.Add(string.Empty);
        lines.Add("Quality checklist:");
        lines.AddRange(QualityChecklist.Select(static item => $"- {item}"));

        return string.Join(Environment.NewLine, lines);
    }
}
