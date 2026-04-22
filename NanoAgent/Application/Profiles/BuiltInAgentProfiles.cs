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
        "Default coding agent profile for editing, shell toolchain work, build, and test loops.",
        """
        Active agent profile: build.
        Behave as a normal coding agent. You may inspect, edit, run approved shell/toolchain commands, build, and test when useful.
        Respect the tool permission system and keep plans synchronized for non-trivial work.
        """,
        AllTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.AllowEdits,
            AgentProfileShellMode.Default,
            "Normal coding-agent behavior with edits and toolchain execution governed by permissions."));

    public static IAgentProfile Plan { get; } = new BuiltInAgentProfile(
        PlanName,
        "Read-only planning profile for repo inspection, safe shell probes, and implementation plans.",
        """
        Active agent profile: plan.
        Stay read-only. Inspect files, search the workspace, run safe shell inspection/probe commands only, and produce a concrete plan.
        Do not patch, write files, or perform mutating operations.
        """,
        InspectionTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.ReadOnly,
            AgentProfileShellMode.SafeInspectionOnly,
            "Read-only planning and inspection; no edits or mutating shell work."));

    public static IAgentProfile Review { get; } = new BuiltInAgentProfile(
        ReviewName,
        "Read/search/inspect profile for code review-style findings without edits by default.",
        """
        Active agent profile: review.
        Prioritize findings, risks, regressions, and missing tests. Stay non-editing by default.
        Use read/search tools and safe inspection commands; do not patch or write files.
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
