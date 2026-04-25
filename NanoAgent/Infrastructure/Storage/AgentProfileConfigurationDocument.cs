using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class AgentProfileConfigurationDocument
{
    public AgentProviderProfile? ProviderProfile { get; set; }

    public string? PreferredModelId { get; set; }

    public string? ReasoningEffort { get; set; }

    public MemoryProfileDocument? Memory { get; set; }

    public Dictionary<string, McpServerProfileDocument>? McpServers { get; set; }
}

internal sealed class MemoryProfileDocument
{
    public bool? AllowAutoFailureObservation { get; set; }

    public bool? AllowAutoManualLessons { get; set; }

    public bool? Disabled { get; set; }

    public int? MaxEntries { get; set; }

    public int? MaxPromptChars { get; set; }

    public bool? RedactSecrets { get; set; }

    public bool? RequireApprovalForWrites { get; set; }
}

internal sealed class McpServerProfileDocument
{
    public List<string>? Args { get; set; }

    public string? BearerTokenEnvVar { get; set; }

    public string? Command { get; set; }

    public string? Cwd { get; set; }

    public string? DefaultToolsApprovalMode { get; set; }

    public List<string>? DisabledTools { get; set; }

    public bool? Enabled { get; set; }

    public List<string>? EnabledTools { get; set; }

    public Dictionary<string, string>? Env { get; set; }

    public Dictionary<string, string>? EnvHttpHeaders { get; set; }

    public List<string>? EnvVars { get; set; }

    public Dictionary<string, string>? HttpHeaders { get; set; }

    public bool? Required { get; set; }

    public int? StartupTimeoutSeconds { get; set; }

    public Dictionary<string, McpToolProfileDocument>? Tools { get; set; }

    public Dictionary<string, string>? ToolApprovalModes { get; set; }

    public int? ToolTimeoutSeconds { get; set; }

    public string? Url { get; set; }
}

internal sealed class McpToolProfileDocument
{
    public string? ApprovalMode { get; set; }
}
