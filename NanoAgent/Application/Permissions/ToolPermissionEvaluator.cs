using System.Text.Json;
using System.Text.RegularExpressions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Planning;

namespace NanoAgent.Application.Permissions;

internal sealed class ToolPermissionEvaluator : IPermissionEvaluator
{
    private readonly IPermissionConfigurationAccessor _configurationAccessor;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public ToolPermissionEvaluator(
        IWorkspaceRootProvider workspaceRootProvider,
        IPermissionConfigurationAccessor configurationAccessor)
    {
        _workspaceRootProvider = workspaceRootProvider;
        _configurationAccessor = configurationAccessor;
    }

    public PermissionEvaluationResult Evaluate(
        ToolPermissionPolicy permissionPolicy,
        PermissionEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(permissionPolicy);
        ArgumentNullException.ThrowIfNull(context);

        PermissionEvaluationResult? planningModeResult = PlanningModePolicy.EvaluateRestrictions(
            permissionPolicy,
            context.ToolExecutionContext);
        if (planningModeResult is not null)
        {
            return planningModeResult;
        }

        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        List<string> subjects = [];

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
                context.ToolExecutionContext,
                permissionPolicy.Shell,
                subjects);

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

        PermissionRequestDescriptor request = CreateRequestDescriptor(
            context.ToolExecutionContext,
            permissionPolicy,
            subjects);

        PermissionMode effectiveMode = DetermineEffectiveMode(
            request,
            context.ToolExecutionContext.Session.PermissionOverrides,
            GetFallbackMode(permissionPolicy.ApprovalMode));

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
        PermissionSettings settings = _configurationAccessor.GetSettings();
        PermissionRule[] rules = (settings.Rules ?? [])
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

    private static PermissionMode GetFallbackMode(ToolApprovalMode approvalMode)
    {
        return approvalMode == ToolApprovalMode.RequireApproval
            ? PermissionMode.Ask
            : PermissionMode.Allow;
    }

    private static PermissionRequestDescriptor CreateRequestDescriptor(
        ToolExecutionContext context,
        ToolPermissionPolicy permissionPolicy,
        IReadOnlyList<string> subjects)
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

    private PermissionEvaluationResult EvaluateFilePathRule(
        ToolExecutionContext context,
        string workspaceRoot,
        FilePathPermissionRule rule,
        List<string> subjects)
    {
        if (!TryGetOptionalString(context.Arguments, rule.ArgumentName, out string? requestedPath))
        {
            return PermissionEvaluationResult.Allowed();
        }

        string candidatePath;
        try
        {
            candidatePath = ResolveWithinWorkspace(workspaceRoot, requestedPath!);
        }
        catch (InvalidOperationException)
        {
            return PermissionEvaluationResult.Denied(
                "path_outside_workspace",
                $"Tool '{context.ToolName}' cannot use path '{requestedPath}' because it resolves outside the current workspace.");
        }

        bool isAllowed = rule.AllowedRoots.Any(allowedRoot =>
        {
            string allowedPath = ResolveWithinWorkspace(workspaceRoot, allowedRoot);
            return IsSamePathOrDescendant(allowedPath, candidatePath);
        });

        if (!isAllowed)
        {
            string allowedRoots = string.Join(", ", rule.AllowedRoots);
            return PermissionEvaluationResult.Denied(
                "path_not_allowed",
                $"Tool '{context.ToolName}' was denied {rule.Kind.ToString().ToLowerInvariant()} access to '{requestedPath}'. Allowed roots: {allowedRoots}.");
        }

        subjects.Add(ToWorkspaceRelativePath(workspaceRoot, candidatePath));
        return PermissionEvaluationResult.Allowed();
    }

    private PermissionEvaluationResult EvaluatePatchPolicy(
        ToolExecutionContext context,
        string workspaceRoot,
        PatchPermissionPolicy patchPolicy,
        List<string> subjects)
    {
        if (!TryGetOptionalString(context.Arguments, patchPolicy.PatchArgumentName, out string? patchText))
        {
            return PermissionEvaluationResult.Allowed();
        }

        foreach (string path in ExtractPatchPaths(patchText!))
        {
            string candidatePath;
            try
            {
                candidatePath = ResolveWithinWorkspace(workspaceRoot, path);
            }
            catch (InvalidOperationException)
            {
                return PermissionEvaluationResult.Denied(
                    "path_outside_workspace",
                    $"Tool '{context.ToolName}' cannot modify '{path}' because it resolves outside the current workspace.");
            }

            bool isAllowed = patchPolicy.AllowedRoots.Any(allowedRoot =>
            {
                string allowedPath = ResolveWithinWorkspace(workspaceRoot, allowedRoot);
                return IsSamePathOrDescendant(allowedPath, candidatePath);
            });

            if (!isAllowed)
            {
                string allowedRoots = string.Join(", ", patchPolicy.AllowedRoots);
                return PermissionEvaluationResult.Denied(
                    "path_not_allowed",
                    $"Tool '{context.ToolName}' was denied {patchPolicy.Kind.ToString().ToLowerInvariant()} access to '{path}'. Allowed roots: {allowedRoots}.");
            }

            subjects.Add(ToWorkspaceRelativePath(workspaceRoot, candidatePath));
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

    private static PermissionEvaluationResult EvaluateShellPolicy(
        ToolExecutionContext context,
        ShellCommandPermissionPolicy shellPolicy,
        List<string> subjects)
    {
        if (!TryGetOptionalString(context.Arguments, shellPolicy.CommandArgumentName, out string? commandText))
        {
            return PermissionEvaluationResult.Allowed();
        }

        string? commandName = ExtractCommandName(commandText!);
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return PermissionEvaluationResult.Denied(
                "invalid_shell_command",
                $"Tool '{context.ToolName}' did not receive a valid shell command.");
        }

        bool isAllowed = shellPolicy.AllowedCommands.Contains(
            commandName,
            StringComparer.OrdinalIgnoreCase);

        if (!isAllowed)
        {
            string allowedCommands = string.Join(", ", shellPolicy.AllowedCommands);
            return PermissionEvaluationResult.Denied(
                "shell_command_not_allowed",
                $"Tool '{context.ToolName}' cannot execute shell command '{commandName}'. Allowed commands: {allowedCommands}.");
        }

        subjects.Add(commandText!.Trim());
        return PermissionEvaluationResult.Allowed();
    }

    private static void AddWebRequestSubject(
        ToolExecutionContext context,
        WebRequestPermissionPolicy webRequestPolicy,
        List<string> subjects)
    {
        if (TryGetOptionalString(context.Arguments, webRequestPolicy.RequestArgumentName, out string? requestValue))
        {
            subjects.Add(requestValue!);
        }
    }

    private static string? ExtractCommandName(string commandText)
    {
        ReadOnlySpan<char> value = commandText.AsSpan().TrimStart();
        if (value.IsEmpty)
        {
            return null;
        }

        char firstCharacter = value[0];
        if (firstCharacter is '"' or '\'')
        {
            int closingQuoteIndex = value[1..].IndexOf(firstCharacter);
            if (closingQuoteIndex < 0)
            {
                return null;
            }

            return NormalizeCommandToken(value.Slice(1, closingQuoteIndex).ToString());
        }

        int separatorIndex = value.IndexOfAny(" \t\r\n".AsSpan());
        string token = separatorIndex < 0
            ? value.ToString()
            : value[..separatorIndex].ToString();

        return NormalizeCommandToken(token);
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

    private static string ResolveWithinWorkspace(
        string workspaceRoot,
        string requestedPath)
    {
        string fullPath = Path.GetFullPath(
            Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(workspaceRoot, requestedPath));

        if (!IsSamePathOrDescendant(workspaceRoot, fullPath))
        {
            throw new InvalidOperationException("The requested path is outside the workspace.");
        }

        return fullPath;
    }

    private static string ToWorkspaceRelativePath(
        string workspaceRoot,
        string fullPath)
    {
        string relativePath = Path.GetRelativePath(workspaceRoot, fullPath);
        return relativePath == "."
            ? "."
            : relativePath.Replace('\\', '/');
    }

    private static bool IsSamePathOrDescendant(
        string parentPath,
        string candidatePath)
    {
        string normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
        string normalizedCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidatePath));

        return normalizedCandidate.StartsWith(
                   normalizedParent,
                   GetPathComparison()) ||
               string.Equals(
                   Path.GetFullPath(parentPath),
                   Path.GetFullPath(candidatePath),
                   GetPathComparison());
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ||
               path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
