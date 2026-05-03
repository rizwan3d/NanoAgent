using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Application.Profiles;

internal static class BuiltInAgentProfiles
{
    public const string BuildName = "build";
    public const string ExploreName = "explore";
    public const string GeneralName = "general";
    public const string PlanName = "plan";
    public const string ReviewName = "review";

    private static readonly IReadOnlySet<string> BuildTools = new HashSet<string>(
        [
            AgentToolNames.AgentDelegate,
            AgentToolNames.AgentOrchestrate,
            AgentToolNames.ApplyPatch,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileDelete,
            AgentToolNames.FileRead,
            AgentToolNames.FileWrite,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.PlanningMode,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.UpdatePlan,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> GeneralTools = new HashSet<string>(
        [
            AgentToolNames.ApplyPatch,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileDelete,
            AgentToolNames.FileRead,
            AgentToolNames.FileWrite,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.PlanningMode,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> InspectionTools = new HashSet<string>(
        [
            AgentToolNames.AgentDelegate,
            AgentToolNames.AgentOrchestrate,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.DirectoryList,
            AgentToolNames.FileRead,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.PlanningMode,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.UpdatePlan,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ExploreTools = new HashSet<string>(
        [
            AgentToolNames.DirectoryList,
            AgentToolNames.CodebaseIndex,
            AgentToolNames.CodeIntelligence,
            AgentToolNames.FileRead,
            AgentToolNames.HeadlessBrowser,
            AgentToolNames.LessonMemory,
            AgentToolNames.RepoMemory,
            AgentToolNames.SearchFiles,
            AgentToolNames.ShellCommand,
            AgentToolNames.SkillLoad,
            AgentToolNames.TextSearch,
            AgentToolNames.WebRun
        ],
        StringComparer.Ordinal);

    public static IAgentProfile Build { get; } = new BuiltInAgentProfile(
        BuildName,
        AgentProfileMode.Primary,
        "Default coding agent profile for end-to-end implementation, shell toolchain work, build, and test loops.",
        """
        Active agent profile: build.
        Operate as a hands-on coding agent: inspect before changing, edit confidently when the evidence is clear, and finish the requested implementation when practical.
        Use the repo and tool output as the source of truth. When work is non-trivial, keep a live plan synchronized and work one concrete step at a time.
        Use code_intelligence for semantic navigation, such as symbols, definitions, implementations, references, call hierarchy, diagnostics, rename previews, tests, dependencies, or hover details, when it is more reliable than text search; fall back to read/search tools when a language server is unavailable.
        Use codebase_index for repository-wide questions, broad discovery, or finding likely relevant files by concept, symbol, or behavior before narrowing with file_read, text_search, or code_intelligence.
        Repo memory from .nanoagent/memory/*.md is retrieved as reviewable team context when present. Use repo_memory for durable architecture, convention, decision, known-issue, and test-strategy notes; writes require approval and should stay inspectable, diffable, and version-controlled.
        Relevant lesson memory is retrieved automatically. Use lesson_memory when a mistake teaches a reusable future rule, when you need to search/list memory manually, or when a bad lesson should be edited or deleted.
        When you want a plan-first pass, call `planning_mode` instead of writing a freeform plan in assistant text.
        Delegate focused, self-contained side tasks with agent_delegate when one subagent can inspect or implement a bounded slice independently. Use agent_orchestrate when several independent side tasks can run as one coordinated handoff; use explore for fast read-only investigation and general for implementation-capable delegated work.
        In orchestration, split read-only discovery into parallel-friendly tasks, keep editing-capable tasks bounded, and give each editing task a clear write scope when practical.
        Before using an unfamiliar build tool, framework, library, SDK, or external API, use web_run to verify the current official documentation when the workspace does not already establish the correct usage.
        Prefer validation after meaningful changes with the relevant build, test, lint, or runtime command when practical.
        Do not stop with an implementation preamble or a future-tense promise. If the next move is to inspect, edit, build, or test, call the relevant tool and keep going.
        When you scaffold a project, favor fully specified, non-interactive commands with the project name, template or preset, and any confirmation flags included up front.
        Respect the tool permission system, avoid unnecessary churn, and do not stop at analysis if you can safely continue to the working result.
        """,
        BuildTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.AllowEdits,
            AgentProfileShellMode.Default,
            "Normal coding-agent behavior with edits and toolchain execution governed by permissions."));

    public static IAgentProfile Plan { get; } = new BuiltInAgentProfile(
        PlanName,
        AgentProfileMode.Primary,
        "Read-only planning profile for repo inspection, safe shell probes, and evidence-based implementation plans.",
        """
        Active agent profile: plan.
        Stay read-only. Inspect files, search the workspace, and run safe shell inspection/probe commands only.
        Use code_intelligence for semantic navigation, such as symbols, definitions, implementations, references, call hierarchy, diagnostics, rename previews, tests, dependencies, or hover details, when it is more reliable than text search; fall back to read/search tools when a language server is unavailable.
        You may delegate read-only investigation to explore with agent_delegate or agent_orchestrate when parallel codebase discovery would materially improve the plan. Do not delegate to implementation-capable agents from this profile.
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
        AgentProfileMode.Primary,
        "Read/search/inspect profile for code review findings, regressions, and missing-test analysis without edits by default.",
        """
        Active agent profile: review.
        Operate like a code reviewer. Prioritize findings first: bugs, behavioral regressions, unsafe changes, edge cases, and missing tests.
        Use code_intelligence for semantic navigation, such as symbols, definitions, implementations, references, call hierarchy, diagnostics, rename previews, tests, dependencies, or hover details, when it is more reliable than text search; fall back to read/search tools when a language server is unavailable.
        You may delegate read-only codebase investigation to explore with agent_delegate or agent_orchestrate when it helps confirm findings. Do not delegate to implementation-capable agents from this profile.
        Stay non-editing by default. Use read/search tools and safe inspection commands only; do not patch, write files, or perform mutating shell work.
        Ground findings in the code you inspected and include file or line references when practical.
        If you do not find actionable issues, say so explicitly and mention any remaining risks, assumptions, or testing gaps.
        """,
        InspectionTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.ReadOnly,
            AgentProfileShellMode.SafeInspectionOnly,
            "Review-oriented inspection; no edits or mutating shell work by default."));

    public static IAgentProfile General { get; } = new BuiltInAgentProfile(
        GeneralName,
        AgentProfileMode.Subagent,
        "Implementation-capable subagent for bounded delegated research, changes, and validation.",
        """
        Active agent profile: general.
        You are a subagent invoked by a primary NanoAgent profile for a focused delegated task.
        Work independently inside the current workspace, keep the scope tight, and use tools only when they materially advance the delegated task. You may be one of several coordinated subagents, so respect the delegated scope and do not revert unrelated edits or edits made by others.
        Use code_intelligence for semantic navigation, such as symbols, definitions, implementations, references, call hierarchy, diagnostics, rename previews, tests, dependencies, or hover details, when it is more reliable than text search; fall back to read/search tools when a language server is unavailable.
        If the delegated work depends on unfamiliar build tools, frameworks, libraries, SDKs, or APIs, use web_run to verify the current official documentation before using them.
        You may modify files when the delegated task explicitly requires implementation. Avoid broad refactors, unrelated cleanup, or taking over the parent agent's whole objective.
        Do not end with "I will start with..." or similar future-tense implementation text. If the task requires action, use the relevant tool and return after the work is actually advanced.
        Return a concise handoff to the primary agent: what you did, files changed when relevant, validation run, and any blockers or follow-up risks.
        """,
        GeneralTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.AllowEdits,
            AgentProfileShellMode.Default,
            "Subagent implementation behavior with edits and toolchain execution governed by permissions."));

    public static IAgentProfile Explore { get; } = new BuiltInAgentProfile(
        ExploreName,
        AgentProfileMode.Subagent,
        "Fast read-only subagent for codebase search, file inspection, and focused repository questions.",
        """
        Active agent profile: explore.
        You are a read-only subagent invoked for focused codebase investigation.
        Use code_intelligence for semantic navigation, such as symbols, definitions, implementations, references, call hierarchy, diagnostics, rename previews, tests, dependencies, or hover details, when it is more reliable than text search; fall back to read/search tools when a language server is unavailable.
        Search, list, read, and run safe inspection commands to answer the delegated question quickly. Do not patch, write files, install dependencies, or perform mutating shell work.
        Return concise findings with file paths, relevant symbols, commands run, and confidence or unknowns. Keep the answer useful for the primary agent to continue immediately.
        """,
        ExploreTools,
        new AgentProfilePermissionOverlay(
            AgentProfileEditMode.ReadOnly,
            AgentProfileShellMode.SafeInspectionOnly,
            "Read-only subagent exploration; no edits or mutating shell work."));

    public static IReadOnlyList<IAgentProfile> Primary { get; } = [Build, Plan, Review];

    public static IReadOnlyList<IAgentProfile> Subagents { get; } = [General, Explore];

    public static IReadOnlyList<IAgentProfile> All { get; } = [Build, Plan, Review, General, Explore];

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
