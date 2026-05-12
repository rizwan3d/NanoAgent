using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal sealed record WindowsSandboxPolicy(
    ToolSandboxMode Mode,
    IReadOnlyList<string> WritableRoots,
    bool IncludeTempEnvironmentVariables)
{
    public static WindowsSandboxPolicy Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Sandbox policy must be provided.", nameof(value));
        }

        string trimmed = value.Trim();
        return trimmed switch
        {
            "read-only" => new WindowsSandboxPolicy(ToolSandboxMode.ReadOnly, [], IncludeTempEnvironmentVariables: false),
            "workspace-write" => new WindowsSandboxPolicy(ToolSandboxMode.WorkspaceWrite, [], IncludeTempEnvironmentVariables: true),
            "danger-full-access" or "external-sandbox" => throw UnsupportedPolicy(trimmed),
            _ => ParseJson(trimmed)
        };
    }

    private static WindowsSandboxPolicy ParseJson(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        JsonElement root = document.RootElement;
        string? policyType = ReadPolicyType(root);
        if (string.IsNullOrWhiteSpace(policyType))
        {
            throw new InvalidOperationException("Unsupported Windows sandbox policy JSON: missing policy type.");
        }

        if (string.Equals(policyType, "danger-full-access", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(policyType, "dangerFullAccess", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(policyType, "external-sandbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(policyType, "externalSandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedPolicy(policyType);
        }

        if (string.Equals(policyType, "read-only", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(policyType, "readOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsSandboxPolicy(ToolSandboxMode.ReadOnly, [], IncludeTempEnvironmentVariables: false);
        }

        if (!string.Equals(policyType, "workspace-write", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(policyType, "workspaceWrite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported Windows sandbox policy '{policyType}'.");
        }

        List<string> writableRoots = [];
        if (root.TryGetProperty("writable_roots", out JsonElement writableRootsElement) ||
            root.TryGetProperty("writableRoots", out writableRootsElement))
        {
            foreach (JsonElement item in writableRootsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    writableRoots.Add(item.GetString()!);
                }
            }
        }

        bool includeTemp = true;
        if (root.TryGetProperty("exclude_tmpdir_env_var", out JsonElement excludeTmpElement) ||
            root.TryGetProperty("excludeTmpdirEnvVar", out excludeTmpElement))
        {
            includeTemp = excludeTmpElement.ValueKind != JsonValueKind.True;
        }

        return new WindowsSandboxPolicy(ToolSandboxMode.WorkspaceWrite, writableRoots, includeTemp);
    }

    private static string? ReadPolicyType(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.String)
        {
            return root.GetString();
        }

        if (root.TryGetProperty("type", out JsonElement typeElement) &&
            typeElement.ValueKind == JsonValueKind.String)
        {
            return typeElement.GetString();
        }

        if (root.TryGetProperty("mode", out JsonElement modeElement) &&
            modeElement.ValueKind == JsonValueKind.String)
        {
            return modeElement.GetString();
        }

        return null;
    }

    private static InvalidOperationException UnsupportedPolicy(string policy)
    {
        return new InvalidOperationException(
            $"Windows sandbox rejects unsupported policy '{policy}'. danger-full-access and external-sandbox are not sandboxed modes.");
    }
}
