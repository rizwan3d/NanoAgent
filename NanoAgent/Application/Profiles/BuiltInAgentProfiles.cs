using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Application.Profiles;

internal static class BuiltInAgentProfiles
{
    public const string BuildName = "build";
    public const string PlanName = "plan";
    public const string ReviewName = "review";

    private static readonly IReadOnlySet<string> AllTools = new HashSet<string>(
        [
            AgentToolNames.ApplyPatch,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileRead,
            AgentToolNames.FileWrite,
            AgentToolNames.PlanningMode,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.TextSearch,
            AgentToolNames.UpdatePlan,
            AgentToolNames.WebSearch
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> InspectionTools = new HashSet<string>(
        [
            AgentToolNames.DirectoryList,
            AgentToolNames.FileRead,
            AgentToolNames.PlanningMode,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.TextSearch,
            AgentToolNames.UpdatePlan,
            AgentToolNames.WebSearch
        ],
        StringComparer.Ordinal);

    public static IAgentProfile Build { get; } = new BuiltInAgentProfile(
        BuildName,
        "Default coding agent profile for end-to-end implementation, shell toolchain work, build, and test loops.",
        """
        Active agent profile: build.
        Operate as a hands-on coding agent: inspect before changing, edit confidently when the evidence is clear, and finish the requested implementation when practical.
        Use the repo and tool output as the source of truth. When work is non-trivial, keep a live plan synchronized and work one concrete step at a time.
        Prefer validation after meaningful changes with the relevant build, test, lint, or runtime command when practical.
        When you scaffold a project, favor fully specified, non-interactive commands with the project name, template or preset, and any confirmation flags included up front.
        Respect the tool permission system, avoid unnecessary churn, and do not stop at analysis if you can safely continue to the working result.
        """,
        AllTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.AllowEdits,
            AgentProfileShellMode.Default,
            "Normal coding-agent behavior with edits and toolchain execution governed by permissions."));

    public static IAgentProfile Plan { get; } = new BuiltInAgentProfile(
        PlanName,
        "Read-only planning profile for repo inspection, safe shell probes, and evidence-based implementation plans.",
        """
        Active agent profile: plan.
        Stay read-only. Inspect files, search the workspace, and run safe shell inspection/probe commands only.
        Produce an evidence-based implementation plan, not a vague outline: separate verified facts from assumptions or open questions, identify the likely files, commands, toolchains, and validation path, and keep the immediate next step explicit.
        When there is a meaningful tradeoff, compare the realistic options briefly and recommend the best path.
        Do not patch, write files, install dependencies, or perform other mutating operations.
        """,
        InspectionTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.ReadOnly,
            AgentProfileShellMode.SafeInspectionOnly,
            "Read-only planning and inspection; no edits or mutating shell work."));

    public static IAgentProfile Review { get; } = new BuiltInAgentProfile(
        ReviewName,
        "Read/search/inspect profile for code review findings, regressions, and missing-test analysis without edits by default.",
        """
        Active agent profile: review.
        Operate like a code reviewer. Prioritize findings first: bugs, behavioral regressions, unsafe changes, edge cases, and missing tests.
        Stay non-editing by default. Use read/search tools and safe inspection commands only; do not patch, write files, or perform mutating shell work.
        Ground findings in the code you inspected and include file or line references when practical.
        If you do not find actionable issues, say so explicitly and mention any remaining risks, assumptions, or testing gaps.
        """,
        InspectionTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.ReadOnly,
            AgentProfileShellMode.SafeInspectionOnly,
            "Review-oriented inspection; no edits or mutating shell work by default."));

    public static IReadOnlyList<IAgentProfile> All { get; } = [Build, Plan, Review];

    public static IAgentProfile Resolve(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return Build;
        }

        string normalizedProfileName = profileName.Trim();
        return All.FirstOrDefault(profile =>
            string.Equals(profile.Name, normalizedProfileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown agent profile '{normalizedProfileName}'. Available profiles: {string.Join(", ", All.Select(static profile => profile.Name))}.",
                nameof(profileName));
    }
}
