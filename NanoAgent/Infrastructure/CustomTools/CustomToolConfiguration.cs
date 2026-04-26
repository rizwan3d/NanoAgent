using System.Text.Json;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;

namespace NanoAgent.Infrastructure.CustomTools;

internal sealed class CustomToolConfiguration
{
    public CustomToolConfiguration(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public List<string> Args { get; } = [];

    public string? ApprovalMode { get; set; }

    public string? Command { get; set; }

    public string? Cwd { get; set; }

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    public Dictionary<string, string> Env { get; } = new(StringComparer.Ordinal);

    public int MaxOutputChars { get; set; } = 24_000;

    public string Name { get; }

    public JsonElement? Schema { get; set; }

    public string? SourcePath { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public ToolApprovalMode GetApprovalMode()
    {
        return ApprovalMode?.Trim().ToLowerInvariant() switch
        {
            "approve" or "auto" or "automatic" => ToolApprovalMode.Automatic,
            "prompt" or "ask" or "requireapproval" => ToolApprovalMode.RequireApproval,
            _ => ToolApprovalMode.RequireApproval
        };
    }

    public string GetDescription()
    {
        return string.IsNullOrWhiteSpace(Description)
            ? $"Run configured custom tool '{Name}'. The tool is implemented by a local process and receives JSON arguments on stdin."
            : Description.Trim();
    }

    public string GetSchema()
    {
        return Schema is { } schema
            ? schema.GetRawText()
            : """{ "type": "object", "properties": {}, "additionalProperties": true }""";
    }

    public void ResolveRelativePaths(string workspaceRoot)
    {
        if (!string.IsNullOrWhiteSpace(Cwd) &&
            !Path.IsPathRooted(Cwd))
        {
            Cwd = WorkspacePath.Resolve(workspaceRoot, Cwd);
        }

        if (string.IsNullOrWhiteSpace(Command) ||
            Path.IsPathRooted(Command) ||
            !LooksLikeRelativePath(Command))
        {
            return;
        }

        Command = WorkspacePath.Resolve(workspaceRoot, Command);
    }

    private static bool LooksLikeRelativePath(string value)
    {
        return value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.StartsWith(".", StringComparison.Ordinal);
    }
}
