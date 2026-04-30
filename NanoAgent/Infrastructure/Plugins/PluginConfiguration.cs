using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Plugins;

internal sealed class PluginConfiguration
{
    private readonly HashSet<string> _assignedProperties = new(StringComparer.Ordinal);

    public PluginConfiguration(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public string? ApprovalMode { get; set; }

    public bool Enabled { get; set; } = true;

    public string Name { get; }

    public bool Required { get; set; }

    public Dictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    public string? SourcePath { get; set; }

    public Dictionary<string, string> ToolApprovalModes { get; } = new(StringComparer.Ordinal);

    public ToolApprovalMode GetApprovalMode(string toolName)
    {
        string? configuredMode = ToolApprovalModes.TryGetValue(toolName, out string? toolMode)
            ? toolMode
            : ApprovalMode;

        return configuredMode?.Trim().ToLowerInvariant() switch
        {
            "approve" or "auto" or "automatic" => ToolApprovalMode.Automatic,
            "prompt" or "ask" or "requireapproval" => ToolApprovalMode.RequireApproval,
            _ => ToolApprovalMode.RequireApproval
        };
    }

    public string? GetSetting(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Settings.TryGetValue(name.Trim(), out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    public void Mark(string propertyName)
    {
        _assignedProperties.Add(propertyName);
    }

    public void Merge(PluginConfiguration other)
    {
        ArgumentNullException.ThrowIfNull(other);

        SourcePath = other.SourcePath ?? SourcePath;

        if (other.IsAssigned(nameof(ApprovalMode)))
        {
            ApprovalMode = other.ApprovalMode;
        }

        if (other.IsAssigned(nameof(Enabled)))
        {
            Enabled = other.Enabled;
        }

        if (other.IsAssigned(nameof(Required)))
        {
            Required = other.Required;
        }

        if (other.IsAssigned(nameof(Settings)))
        {
            foreach (KeyValuePair<string, string> item in other.Settings)
            {
                Settings[item.Key] = item.Value;
            }
        }

        if (other.IsAssigned(nameof(ToolApprovalModes)))
        {
            foreach (KeyValuePair<string, string> item in other.ToolApprovalModes)
            {
                ToolApprovalModes[item.Key] = item.Value;
            }
        }
    }

    private bool IsAssigned(string propertyName)
    {
        return _assignedProperties.Contains(propertyName);
    }
}
