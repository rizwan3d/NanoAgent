using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Planning;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NanoAgent.Application.Permissions;

internal sealed class ToolPermissionEvaluator : IPermissionEvaluator
{
    private static readonly HashSet<string> MemoryWriteActions = new(
        ["save", "edit", "delete"],
        StringComparer.OrdinalIgnoreCase);

    private readonly MemorySettings _memorySettings;
    private readonly PermissionSettings _settings;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public ToolPermissionEvaluator(
        IWorkspaceRootProvider workspaceRootProvider,
        PermissionSettings settings,
        MemorySettings? memorySettings = null)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _settings = settings;
        _memorySettings = memorySettings ?? new MemorySettings();
    }

    public PermissionEvaluationResult Evaluate(
        ToolPermissionPolicy permissionPolicy,
        PermissionEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(permissionPolicy);
        ArgumentNullException.ThrowIfNull(context);

        PermissionEvaluationResult? profileResult = PlanningModePolicy.EvaluateProfileRestrictions(
            permissionPolicy,
            context.ToolExecutionContext);
        if (profileResult is not null)
        {
            return profileResult;
        }

        PermissionEvaluationResult? planningModeResult = PlanningModePolicy.EvaluateRestrictions(
            permissionPolicy,
            context.ToolExecutionContext);
        if (planningModeResult is not null)
        {
            return planningModeResult;
        }

        PermissionEvaluationResult? sandboxModeResult = EvaluateSandboxModeRestrictions(
            permissionPolicy,
            context.ToolExecutionContext);
        if (sandboxModeResult is not null)
        {
            return sandboxModeResult;
        }

        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        List<string> subjects = [];
        List<string> dynamicToolTags = [];

        foreach (FilePathPermissionRule rule in permissionPolicy.FilePaths)
        {
            PermissionEvaluationResult result = EvaluateFilePathRule(
                context.ToolExecutionContext,
                workspaceRoot,
                rule,
                subjects);

            if (!result.IsAllowed)
            {
                return result;
            }
        }

        if (permissionPolicy.Patch is not null)
        {
            PermissionEvaluationResult patchResult = EvaluatePatchPolicy(
                context.ToolExecutionContext,
                workspaceRoot,
                permissionPolicy.Patch,
                subjects);

            if (!patchResult.IsAllowed)
            {
                return patchResult;
            }
        }

        if (permissionPolicy.Shell is not null)
        {
            PermissionEvaluationResult shellResult = EvaluateShellPolicy(
                context,
                permissionPolicy.Shell,
                subjects,
                dynamicToolTags);

            if (!shellResult.IsAllowed)
            {
                return shellResult;
            }
        }

        if (permissionPolicy.WebRequest is not null)
        {
            AddWebRequestSubject(
                context.ToolExecutionContext,
                permissionPolicy.WebRequest,
                subjects);
        }

        AddMemoryWriteTagIfNeeded(
            permissionPolicy,
            context.ToolExecutionContext,
            dynamicToolTags);

        PermissionRequestDescriptor request = CreateRequestDescriptor(
            context.ToolExecutionContext,
            permissionPolicy,
            subjects,
            dynamicToolTags);

        PermissionEvaluationResult? memoryResult = EvaluateMemoryPolicy(
            permissionPolicy,
            context,
            request);
        if (memoryResult is not null)
        {
            return memoryResult;
        }

        if (permissionPolicy.BypassUserPermissionRules)
        {
            return PermissionEvaluationResult.Allowed(
                PermissionMode.Allow,
                request);
        }

        bool sandboxEscalationRequested = subjects.Contains(
            ShellCommandSandboxArguments.SandboxEscalationSubject,
            StringComparer.OrdinalIgnoreCase);
        PermissionMode effectiveMode = DetermineEffectiveMode(
            request,
            context.ToolExecutionContext.Session.PermissionOverrides,
            GetFallbackMode(permissionPolicy.ApprovalMode, sandboxEscalationRequested));

        return effectiveMode switch
        {
            PermissionMode.Allow => PermissionEvaluationResult.Allowed(
                effectiveMode,
                request),
            PermissionMode.Deny => PermissionEvaluationResult.Denied(
                "permission_policy_denied",
                BuildDecisionMessage(request, "denied"),
                effectiveMode,
                request),
            PermissionMode.Ask when !context.ApprovalGranted => PermissionEvaluationResult.RequiresApproval(
                "permission_approval_required",
                BuildDecisionMessage(request, "requires approval for"),
                effectiveMode,
                request),
            _ => PermissionEvaluationResult.Allowed(
                effectiveMode,
                request)
        };
    }

    private PermissionMode DetermineEffectiveMode(
        PermissionRequestDescriptor request,
        IReadOnlyList<PermissionRule> overrides,
        PermissionMode fallbackMode)
    {
        PermissionRule[] rules = (_settings.Rules ?? [])
            .Concat(overrides ?? [])
            .ToArray();

        if (request.Subjects.Count == 0)
        {
            return DetermineModeForSubject(
                request,
                subject: null,
                rules,
                fallbackMode);
        }

        PermissionMode effectiveMode = PermissionMode.Allow;
        foreach (string subject in request.Subjects)
        {
            PermissionMode subjectMode = DetermineModeForSubject(
                request,
                subject,
                rules,
                fallbackMode);

            if (GetSeverity(subjectMode) > GetSeverity(effectiveMode))
            {
                effectiveMode = subjectMode;
            }
        }

        return effectiveMode;
    }

    private static PermissionMode DetermineModeForSubject(
        PermissionRequestDescriptor request,
        string? subject,
        IReadOnlyList<PermissionRule> rules,
        PermissionMode fallbackMode)
    {
        PermissionMode mode = fallbackMode;

        foreach (PermissionRule rule in rules)
        {
            if (!MatchesTool(rule, request))
            {
                continue;
            }

            if (rule.Patterns.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(subject))
                {
                    continue;
                }

                if (!rule.Patterns.Any(pattern => MatchesPattern(subject!, pattern)))
                {
                    continue;
                }
            }

            mode = rule.Mode;
        }

        return mode;
    }

    private static bool MatchesTool(
        PermissionRule rule,
        PermissionRequestDescriptor request)
    {
        if (rule.Tools.Length == 0)
        {
            return true;
        }

        foreach (string pattern in rule.Tools)
        {
            if (MatchesPattern(request.ToolName, pattern) ||
                MatchesPattern(request.ToolKind, pattern) ||
                request.ToolTags.Any(tag => MatchesPattern(tag, pattern)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPattern(
        string value,
        string pattern)
    {
        string normalizedValue = NormalizePatternValue(value);
        string normalizedPattern = NormalizePatternValue(pattern);

        string regexPattern = "^" +
                              Regex.Escape(normalizedPattern)
                                  .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                                  .Replace("\\*", ".*", StringComparison.Ordinal)
                                  .Replace("\\?", ".", StringComparison.Ordinal) +
                              "$";

        return Regex.IsMatch(
            normalizedValue,
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizePatternValue(string value)
    {
        return value
            .Trim()
            .Replace('\\', '/');
    }

    private static int GetSeverity(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Allow => 1,
            PermissionMode.Ask => 2,
            PermissionMode.Deny => 3,
            _ => 0
        };
    }

    private PermissionMode GetFallbackMode(
        ToolApprovalMode approvalMode,
        bool sandboxEscalationRequested)
    {
        PermissionMode fallbackMode = approvalMode == ToolApprovalMode.RequireApproval
            ? PermissionMode.Ask
            : PermissionMode.Allow;

        if (sandboxEscalationRequested &&
            _settings.SandboxMode != ToolSandboxMode.DangerFullAccess &&
            GetSeverity(fallbackMode) < GetSeverity(PermissionMode.Ask))
        {
            return PermissionMode.Ask;
        }

        return fallbackMode;
    }

    private static PermissionRequestDescriptor CreateRequestDescriptor(
        ToolExecutionContext context,
        ToolPermissionPolicy permissionPolicy,
        IReadOnlyList<string> subjects,
        IReadOnlyList<string>? dynamicToolTags = null)
    {
        List<string> tags = [];
        foreach (string tag in permissionPolicy.ToolTags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            string normalizedTag = tag.Trim();
            if (!tags.Contains(normalizedTag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(normalizedTag);
            }
        }

        foreach (string tag in dynamicToolTags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            string normalizedTag = tag.Trim();
            if (!tags.Contains(normalizedTag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(normalizedTag);
            }
        }

        if (!tags.Contains(context.ToolName, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(context.ToolName);
        }

        string toolKind = tags.Count == 0
            ? context.ToolName
            : tags[0];

        return new PermissionRequestDescriptor(
            context.ToolName,
            toolKind,
            tags,
            subjects);
    }

    private static string BuildDecisionMessage(
        PermissionRequestDescriptor request,
        string verb)
    {
        return PermissionRequestDisplayFormatter.BuildDecisionMessage(request, verb);
    }

    private PermissionEvaluationResult? EvaluateSandboxModeRestrictions(
        ToolPermissionPolicy permissionPolicy,
        ToolExecutionContext context)
    {
        if (_settings.SandboxMode != ToolSandboxMode.ReadOnly)
        {
            return null;
        }

        if (PlanningModePolicy.IsWriteLikeTool(permissionPolicy))
        {
            PermissionRequestDescriptor request = CreateRequestDescriptor(
                context,
                permissionPolicy,
                []);

            return PermissionEvaluationResult.Denied(
                "sandbox_readonly_write_blocked",
                $"The configured sandbox mode is read-only. Tool '{context.ToolName}' cannot modify files unless the sandbox mode is changed.",
                PermissionMode.Deny,
                request);
        }

        if (permissionPolicy.Shell is not null &&
            ToolArguments.TryGetNonEmptyString(
                context.Arguments,
                permissionPolicy.Shell.CommandArgumentName,
                out string? commandText) &&
            !PlanningModePolicy.IsSafeInspectionShellCommand(commandText!, out string denialReason) &&
            !ShellRequestsSandboxEscalation(context, permissionPolicy.Shell))
        {
            PermissionRequestDescriptor request = CreateRequestDescriptor(
                context,
                permissionPolicy,
                [commandText!]);

            return PermissionEvaluationResult.Denied(
                "sandbox_readonly_shell_blocked",
                $"The configured sandbox mode is read-only. {denialReason}",
                PermissionMode.Deny,
                request);
        }

        return null;
    }

    private static bool ShellRequestsSandboxEscalation(
        ToolExecutionContext context,
        ShellCommandPermissionPolicy shellPolicy)
    {
        return ShellCommandSandboxArguments.TryGetSandboxPermissions(
                   context.Arguments,
                   shellPolicy.SandboxPermissionsArgumentName,
                   out ShellCommandSandboxPermissions sandboxPermissions,
                   out _) &&
               sandboxPermissions == ShellCommandSandboxPermissions.RequireEscalated;
    }

    private PermissionEvaluationResult EvaluateFilePathRule(
        ToolExecutionContext context,
        string workspaceRoot,
        FilePathPermissionRule rule,
        List<string> subjects)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, rule.ArgumentName, out string? requestedPath))
        {
            return PermissionEvaluationResult.Allowed();
        }

        string sessionRelativePath;
        string candidatePath;
        try
        {
            sessionRelativePath = context.Session.ResolvePathFromWorkingDirectory(requestedPath!);
            candidatePath = WorkspacePath.Resolve(workspaceRoot, sessionRelativePath);
        }
        catch (InvalidOperationException)
        {
            return PermissionEvaluationResult.Denied(
                "path_outside_workspace",
                $"Tool '{context.ToolName}' cannot use path '{requestedPath}' because it resolves outside the current workspace.");
        }

        bool isAllowed = rule.AllowedRoots.Any(allowedRoot =>
        {
            string allowedPath = WorkspacePath.Resolve(workspaceRoot, allowedRoot);
            return WorkspacePath.IsSamePathOrDescendant(allowedPath, candidatePath);
        });

        if (!isAllowed)
        {
            string allowedRoots = string.Join(", ", rule.AllowedRoots);
            return PermissionEvaluationResult.Denied(
                "path_not_allowed",
                $"Tool '{context.ToolName}' was denied {rule.Kind.ToString().ToLowerInvariant()} access to '{requestedPath}'. Allowed roots: {allowedRoots}.");
        }

        subjects.Add(WorkspacePath.ToRelativePath(workspaceRoot, candidatePath));
        return PermissionEvaluationResult.Allowed();
    }

    private PermissionEvaluationResult EvaluatePatchPolicy(
        ToolExecutionContext context,
        string workspaceRoot,
        PatchPermissionPolicy patchPolicy,
        List<string> subjects)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, patchPolicy.PatchArgumentName, out string? patchText))
        {
            return PermissionEvaluationResult.Allowed();
        }

        foreach (string path in ExtractPatchPaths(patchText!))
        {
            string sessionRelativePath;
            string candidatePath;
            try
            {
                sessionRelativePath = context.Session.ResolvePathFromWorkingDirectory(path);
                candidatePath = WorkspacePath.Resolve(workspaceRoot, sessionRelativePath);
            }
            catch (InvalidOperationException)
            {
                return PermissionEvaluationResult.Denied(
                    "path_outside_workspace",
                    $"Tool '{context.ToolName}' cannot modify '{path}' because it resolves outside the current workspace.");
            }

            bool isAllowed = patchPolicy.AllowedRoots.Any(allowedRoot =>
            {
                string allowedPath = WorkspacePath.Resolve(workspaceRoot, allowedRoot);
                return WorkspacePath.IsSamePathOrDescendant(allowedPath, candidatePath);
            });

            if (!isAllowed)
            {
                string allowedRoots = string.Join(", ", patchPolicy.AllowedRoots);
                return PermissionEvaluationResult.Denied(
                    "path_not_allowed",
                    $"Tool '{context.ToolName}' was denied {patchPolicy.Kind.ToString().ToLowerInvariant()} access to '{path}'. Allowed roots: {allowedRoots}.");
            }

            subjects.Add(WorkspacePath.ToRelativePath(workspaceRoot, candidatePath));
        }

        return PermissionEvaluationResult.Allowed();
    }

    private static IReadOnlyList<string> ExtractPatchPaths(string patchText)
    {
        List<string> paths = [];

        foreach (string line in patchText
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string? path = line switch
            {
                _ when line.StartsWith("*** Add File: ", StringComparison.Ordinal) => line["*** Add File: ".Length..].Trim(),
                _ when line.StartsWith("*** Delete File: ", StringComparison.Ordinal) => line["*** Delete File: ".Length..].Trim(),
                _ when line.StartsWith("*** Update File: ", StringComparison.Ordinal) => line["*** Update File: ".Length..].Trim(),
                _ when line.StartsWith("*** Move to: ", StringComparison.Ordinal) => line["*** Move to: ".Length..].Trim(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(path) &&
                !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private PermissionEvaluationResult EvaluateShellPolicy(
        PermissionEvaluationContext permissionContext,
        ShellCommandPermissionPolicy shellPolicy,
        List<string> subjects,
        List<string> dynamicToolTags)
    {
        ToolExecutionContext context = permissionContext.ToolExecutionContext;
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, shellPolicy.CommandArgumentName, out string? commandText))
        {
            return PermissionEvaluationResult.Allowed();
        }

        if (!ShellCommandSandboxArguments.TryGetSandboxPermissions(
                context.Arguments,
                shellPolicy.SandboxPermissionsArgumentName,
                out ShellCommandSandboxPermissions sandboxPermissions,
                out string? invalidSandboxPermissions))
        {
            return PermissionEvaluationResult.Denied(
                "invalid_sandbox_permissions",
                $"Tool '{context.ToolName}' received invalid sandbox permission '{invalidSandboxPermissions}'. Expected 'use_default' or 'require_escalated'.");
        }

        bool requiresEscalation = sandboxPermissions == ShellCommandSandboxPermissions.RequireEscalated;
        if (requiresEscalation &&
            !ToolArguments.TryGetNonEmptyString(
                context.Arguments,
                shellPolicy.JustificationArgumentName,
                out _))
        {
            return PermissionEvaluationResult.Denied(
                "sandbox_justification_required",
                $"Tool '{context.ToolName}' requested escalated sandbox permissions without a justification.");
        }

        if (context.ExecutionPhase == ConversationExecutionPhase.Planning &&
            PlanningModePolicy.ShouldBypassShellPolicyForPlanningProbe(commandText!))
        {
            AddSubject(subjects, commandText!.Trim());
            AddSandboxEscalationSubjectIfNeeded(
                requiresEscalation,
                context,
                shellPolicy,
                subjects,
                dynamicToolTags);
            return PermissionEvaluationResult.Allowed();
        }

        IReadOnlyList<ShellCommandSegment> segments = ShellCommandText.ParseSegments(commandText!);
        if (segments.Count == 0)
        {
            return PermissionEvaluationResult.Denied(
                "invalid_shell_command",
                $"Tool '{context.ToolName}' did not receive a valid shell command.");
        }

        AddSubject(subjects, commandText!.Trim());
        AddSandboxEscalationSubjectIfNeeded(
            requiresEscalation,
            context,
            shellPolicy,
            subjects,
            dynamicToolTags);

        foreach (ShellCommandSegment segment in segments)
        {
            if (!ShellCommandText.TryGetCommandName(segment.CommandText, out _))
            {
                return PermissionEvaluationResult.Denied(
                    "invalid_shell_command",
                    $"Tool '{context.ToolName}' did not receive a valid shell command.");
            }

            AddSubject(subjects, segment.CommandText);
        }

        return PermissionEvaluationResult.Allowed();
    }

    private void AddSandboxEscalationSubjectIfNeeded(
        bool requiresEscalation,
        ToolExecutionContext context,
        ShellCommandPermissionPolicy shellPolicy,
        List<string> subjects,
        List<string> dynamicToolTags)
    {
        if (!requiresEscalation ||
            _settings.SandboxMode == ToolSandboxMode.DangerFullAccess)
        {
            return;
        }

        AddSubject(subjects, ShellCommandSandboxArguments.SandboxEscalationSubject);
        IReadOnlyList<string> prefixRule = ToolArguments.GetOptionalStringArray(
            context.Arguments,
            shellPolicy.PrefixRuleArgumentName);
        if (prefixRule.Count > 0)
        {
            AddSubject(subjects, string.Join(" ", prefixRule) + "*");
        }

        if (!dynamicToolTags.Contains("sandbox", StringComparer.OrdinalIgnoreCase))
        {
            dynamicToolTags.Add("sandbox");
        }
    }

    private PermissionEvaluationResult? EvaluateMemoryPolicy(
        ToolPermissionPolicy permissionPolicy,
        PermissionEvaluationContext context,
        PermissionRequestDescriptor request)
    {
        if (!IsMemoryTool(permissionPolicy, context.ToolExecutionContext))
        {
            return null;
        }

        if (_memorySettings.Disabled)
        {
            return PermissionEvaluationResult.Denied(
                "memory_disabled",
                "Lesson memory is disabled by configuration.",
                PermissionMode.Deny,
                request);
        }

        if (!ToolArguments.TryGetNonEmptyString(
                context.ToolExecutionContext.Arguments,
                "action",
                out string? action) ||
            !MemoryWriteActions.Contains(action!))
        {
            return null;
        }

        if (_settings.MemoryWrite is not null)
        {
            return EvaluateConfiguredMemoryWritePolicy(
                _settings.MemoryWrite.Value,
                context,
                request,
                action!);
        }

        bool requiresApproval =
            _memorySettings.RequireApprovalForWrites ||
            !_memorySettings.AllowAutoManualLessons;
        if (!requiresApproval || context.ApprovalGranted)
        {
            return null;
        }

        return PermissionEvaluationResult.RequiresApproval(
            "memory_write_approval_required",
            $"Tool '{context.ToolExecutionContext.ToolName}' requires approval before it can {action} lesson memory.",
            PermissionMode.Ask,
            request);
    }

    private static PermissionEvaluationResult? EvaluateConfiguredMemoryWritePolicy(
        PermissionMode mode,
        PermissionEvaluationContext context,
        PermissionRequestDescriptor request,
        string action)
    {
        return mode switch
        {
            PermissionMode.Allow => null,
            PermissionMode.Deny => PermissionEvaluationResult.Denied(
                "memory_write_denied",
                $"Tool '{context.ToolExecutionContext.ToolName}' is denied permission to {action} lesson memory.",
                PermissionMode.Deny,
                request),
            PermissionMode.Ask when !context.ApprovalGranted => PermissionEvaluationResult.RequiresApproval(
                "memory_write_approval_required",
                $"Tool '{context.ToolExecutionContext.ToolName}' requires approval before it can {action} lesson memory.",
                PermissionMode.Ask,
                request),
            _ => null
        };
    }

    private static bool IsMemoryTool(
        ToolPermissionPolicy permissionPolicy,
        ToolExecutionContext context)
    {
        return string.Equals(context.ToolName, AgentToolNames.LessonMemory, StringComparison.Ordinal) ||
            (permissionPolicy.ToolTags ?? [])
                .Any(static tag => string.Equals(tag, "memory", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddMemoryWriteTagIfNeeded(
        ToolPermissionPolicy permissionPolicy,
        ToolExecutionContext context,
        List<string> dynamicToolTags)
    {
        if (!IsMemoryTool(permissionPolicy, context) ||
            !ToolArguments.TryGetNonEmptyString(context.Arguments, "action", out string? action) ||
            !MemoryWriteActions.Contains(action!))
        {
            return;
        }

        if (!dynamicToolTags.Contains("memory_write", StringComparer.OrdinalIgnoreCase))
        {
            dynamicToolTags.Add("memory_write");
        }
    }

    private static void AddSubject(
        List<string> subjects,
        string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return;
        }

        string normalizedSubject = subject.Trim();
        if (!subjects.Contains(normalizedSubject, StringComparer.OrdinalIgnoreCase))
        {
            subjects.Add(normalizedSubject);
        }
    }

    private static void AddWebRequestSubject(
        ToolExecutionContext context,
        WebRequestPermissionPolicy webRequestPolicy,
        List<string> subjects)
    {
        if (context.Arguments.TryGetProperty(webRequestPolicy.RequestArgumentName, out JsonElement requestElement))
        {
            AddWebRequestSubjects(requestElement, subjects);
        }
        else if (string.Equals(context.ToolName, AgentToolNames.WebRun, StringComparison.OrdinalIgnoreCase))
        {
            AddWebRequestSubjects(context.Arguments, subjects);
        }
    }

    private static void AddWebRequestSubjects(
        JsonElement element,
        List<string> subjects)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (!string.IsNullOrWhiteSpace(element.GetString()))
                {
                    AddSubject(subjects, element.GetString()!);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AddWebRequestSubjects(item, subjects);
                }

                break;

            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (IsWebRequestSubjectProperty(property.Name))
                    {
                        AddWebRequestSubjects(property.Value, subjects);
                    }
                }

                break;
        }
    }

    private static bool IsWebRequestSubjectProperty(string propertyName)
    {
        return propertyName switch
        {
            "q" or
            "ref_id" or
            "location" or
            "ticker" or
            "utc_offset" or
            "league" or
            "team" or
            "opponent" or
            "date_from" or
            "date_to" => true,
            _ => false
        };
    }

}
