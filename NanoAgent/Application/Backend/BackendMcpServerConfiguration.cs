namespace NanoAgent.Application.Backend;

public sealed class BackendMcpServerConfiguration
{
    private readonly HashSet<string> _assignedProperties = new(StringComparer.Ordinal);

    public BackendMcpServerConfiguration(string name)
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

    public string? Source { get; set; }

    public int StartupTimeoutSeconds { get; set; } = 10;

    public Dictionary<string, string> ToolApprovalModes { get; } = new(StringComparer.Ordinal);

    public int ToolTimeoutSeconds { get; set; } = 60;

    public string? Url { get; set; }

    public bool IsAssigned(string propertyName)
    {
        return _assignedProperties.Contains(propertyName);
    }

    public void Mark(string propertyName)
    {
        _assignedProperties.Add(propertyName);
    }
}
