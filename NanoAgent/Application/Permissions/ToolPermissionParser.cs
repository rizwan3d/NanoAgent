using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Application.Permissions;

internal sealed class ToolPermissionParser : IPermissionParser
{
    public ToolPermissionPolicy Parse(
        string toolName,
        string permissionRequirementsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionRequirementsJson);

        try
        {
            using JsonDocument document = JsonDocument.Parse(permissionRequirementsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Tool '{toolName}' must provide a JSON-object permission policy.");
            }

            ToolPermissionPolicy? policy = JsonSerializer.Deserialize(
                document.RootElement.GetRawText(),
                PermissionJsonContext.Default.ToolPermissionPolicy);

            if (policy is null)
            {
                throw new InvalidOperationException(
                    $"Tool '{toolName}' produced an empty permission policy.");
            }

            return Normalize(toolName, policy);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a valid JSON permission policy.",
                exception);
        }
    }

    private static ToolPermissionPolicy Normalize(
        string toolName,
        ToolPermissionPolicy policy)
    {
        FilePathPermissionRule[] filePathRules = (policy.FilePaths ?? [])
            .Select(rule => NormalizeFilePathRule(toolName, rule))
            .ToArray();

        PatchPermissionPolicy? patchPolicy = policy.Patch is null
            ? null
            : NormalizePatchPolicy(toolName, policy.Patch);

        ShellCommandPermissionPolicy? shellPolicy = policy.Shell is null
            ? null
            : NormalizeShellPolicy(toolName, policy.Shell);

        string[] toolTags = (policy.ToolTags ?? [])
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        WebRequestPermissionPolicy? webRequestPolicy = policy.WebRequest is null
            ? null
            : NormalizeWebRequestPolicy(toolName, policy.WebRequest);

        return new ToolPermissionPolicy
        {
            ApprovalMode = policy.ApprovalMode,
            BypassUserPermissionRules = policy.BypassUserPermissionRules,
            FilePaths = filePathRules,
            Patch = patchPolicy,
            Shell = shellPolicy,
            ToolTags = toolTags,
            WebRequest = webRequestPolicy
        };
    }

    private static FilePathPermissionRule NormalizeFilePathRule(
        string toolName,
        FilePathPermissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.ArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' contains a file-path permission rule without an argument name.");
        }

        string[] allowedRoots = (rule.AllowedRoots ?? [])
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => root.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (allowedRoots.Length == 0)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide at least one allowed root for file-path permission argument '{rule.ArgumentName}'.");
        }

        return new FilePathPermissionRule
        {
            ArgumentName = rule.ArgumentName.Trim(),
            Kind = rule.Kind,
            AllowedRoots = allowedRoots
        };
    }

    private static PatchPermissionPolicy NormalizePatchPolicy(
        string toolName,
        PatchPermissionPolicy patchPolicy)
    {
        ArgumentNullException.ThrowIfNull(patchPolicy);

        if (string.IsNullOrWhiteSpace(patchPolicy.PatchArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a non-empty patch argument name.");
        }

        string[] allowedRoots = (patchPolicy.AllowedRoots ?? [])
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => root.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (allowedRoots.Length == 0)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide at least one allowed root for its patch permission policy.");
        }

        return new PatchPermissionPolicy
        {
            AllowedRoots = allowedRoots,
            Kind = patchPolicy.Kind,
            PatchArgumentName = patchPolicy.PatchArgumentName.Trim()
        };
    }

    private static ShellCommandPermissionPolicy NormalizeShellPolicy(
        string toolName,
        ShellCommandPermissionPolicy shellPolicy)
    {
        ArgumentNullException.ThrowIfNull(shellPolicy);

        if (string.IsNullOrWhiteSpace(shellPolicy.CommandArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a non-empty shell command argument name.");
        }

        if (string.IsNullOrWhiteSpace(shellPolicy.SandboxPermissionsArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a non-empty shell sandbox-permissions argument name.");
        }

        if (string.IsNullOrWhiteSpace(shellPolicy.JustificationArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a non-empty shell justification argument name.");
        }

        if (string.IsNullOrWhiteSpace(shellPolicy.PrefixRuleArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a non-empty shell prefix-rule argument name.");
        }

        return new ShellCommandPermissionPolicy
        {
            CommandArgumentName = shellPolicy.CommandArgumentName.Trim(),
            JustificationArgumentName = shellPolicy.JustificationArgumentName.Trim(),
            PrefixRuleArgumentName = shellPolicy.PrefixRuleArgumentName.Trim(),
            SandboxPermissionsArgumentName = shellPolicy.SandboxPermissionsArgumentName.Trim()
        };
    }

    private static WebRequestPermissionPolicy NormalizeWebRequestPolicy(
        string toolName,
        WebRequestPermissionPolicy webRequestPolicy)
    {
        ArgumentNullException.ThrowIfNull(webRequestPolicy);

        if (string.IsNullOrWhiteSpace(webRequestPolicy.RequestArgumentName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' must provide a non-empty web request argument name.");
        }

        return new WebRequestPermissionPolicy
        {
            RequestArgumentName = webRequestPolicy.RequestArgumentName.Trim()
        };
    }
}
