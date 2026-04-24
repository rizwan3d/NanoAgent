using NanoAgent.Application.Utilities;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class McpServerConfiguration
{
    private readonly HashSet<string> _assignedProperties = new(StringComparer.Ordinal);

    public McpServerConfiguration(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public List<string> Args { get; } = [];

    public string? BearerTokenEnvVar { get; set; }

    public string? Command { get; set; }

    public string? Cwd { get; set; }

    public string? DefaultToolsApprovalMode { get; set; }

    public List<string> DisabledTools { get; } = [];

    public bool Enabled { get; set; } = true;

    public List<string> EnabledTools { get; } = [];

    public Dictionary<string, string> Env { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> EnvHttpHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> EnvVars { get; } = [];

    public Dictionary<string, string> HttpHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }

    public bool Required { get; set; }

    public string? SourcePath { get; set; }

    public int StartupTimeoutSeconds { get; set; } = 10;

    public Dictionary<string, string> ToolApprovalModes { get; } = new(StringComparer.Ordinal);

    public int ToolTimeoutSeconds { get; set; } = 60;

    public string? Url { get; set; }

    public void Mark(string propertyName)
    {
        _assignedProperties.Add(propertyName);
    }

    public void Merge(McpServerConfiguration other)
    {
        ArgumentNullException.ThrowIfNull(other);

        SourcePath = other.SourcePath ?? SourcePath;

        if (other.IsAssigned(nameof(Command)))
        {
            Command = other.Command;
        }

        if (other.IsAssigned(nameof(Args)))
        {
            Args.Clear();
            Args.AddRange(other.Args);
        }

        if (other.IsAssigned(nameof(Env)))
        {
            foreach (KeyValuePair<string, string> item in other.Env)
            {
                Env[item.Key] = item.Value;
            }
        }

        if (other.IsAssigned(nameof(EnvVars)))
        {
            EnvVars.Clear();
            EnvVars.AddRange(other.EnvVars);
        }

        if (other.IsAssigned(nameof(Cwd)))
        {
            Cwd = other.Cwd;
        }

        if (other.IsAssigned(nameof(Url)))
        {
            Url = other.Url;
        }

        if (other.IsAssigned(nameof(BearerTokenEnvVar)))
        {
            BearerTokenEnvVar = other.BearerTokenEnvVar;
        }

        if (other.IsAssigned(nameof(HttpHeaders)))
        {
            foreach (KeyValuePair<string, string> item in other.HttpHeaders)
            {
                HttpHeaders[item.Key] = item.Value;
            }
        }

        if (other.IsAssigned(nameof(EnvHttpHeaders)))
        {
            foreach (KeyValuePair<string, string> item in other.EnvHttpHeaders)
            {
                EnvHttpHeaders[item.Key] = item.Value;
            }
        }

        if (other.IsAssigned(nameof(StartupTimeoutSeconds)))
        {
            StartupTimeoutSeconds = other.StartupTimeoutSeconds;
        }

        if (other.IsAssigned(nameof(ToolTimeoutSeconds)))
        {
            ToolTimeoutSeconds = other.ToolTimeoutSeconds;
        }

        if (other.IsAssigned(nameof(Enabled)))
        {
            Enabled = other.Enabled;
        }

        if (other.IsAssigned(nameof(Required)))
        {
            Required = other.Required;
        }

        if (other.IsAssigned(nameof(EnabledTools)))
        {
            EnabledTools.Clear();
            EnabledTools.AddRange(other.EnabledTools);
        }

        if (other.IsAssigned(nameof(DisabledTools)))
        {
            DisabledTools.Clear();
            DisabledTools.AddRange(other.DisabledTools);
        }

        if (other.IsAssigned(nameof(DefaultToolsApprovalMode)))
        {
            DefaultToolsApprovalMode = other.DefaultToolsApprovalMode;
        }

        if (other.IsAssigned(nameof(ToolApprovalModes)))
        {
            foreach (KeyValuePair<string, string> item in other.ToolApprovalModes)
            {
                ToolApprovalModes[item.Key] = item.Value;
            }
        }
    }

    public void ResolveRelativePaths(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(Cwd) ||
            Path.IsPathRooted(Cwd))
        {
            return;
        }

        Cwd = WorkspacePath.Resolve(workspaceRoot, Cwd);
    }

    public bool ShouldIncludeTool(string toolName)
    {
        if (EnabledTools.Count > 0 &&
            !EnabledTools.Contains(toolName, StringComparer.Ordinal))
        {
            return false;
        }

        return !DisabledTools.Contains(toolName, StringComparer.Ordinal);
    }

    private bool IsAssigned(string propertyName)
    {
        return _assignedProperties.Contains(propertyName);
    }
}
