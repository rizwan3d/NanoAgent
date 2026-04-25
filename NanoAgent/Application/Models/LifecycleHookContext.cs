namespace NanoAgent.Application.Models;

public sealed class LifecycleHookContext
{
    public string EventName { get; set; } = string.Empty;

    public string? ApplicationName { get; set; }

    public string? ArgumentsJson { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorType { get; set; }

    public string? ExecutionPhase { get; set; }

    public long? LatencyMilliseconds { get; set; }

    public string? MemoryAction { get; set; }

    public string? MemoryProblem { get; set; }

    public string? MemoryTrigger { get; set; }

    public string? ModelId { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public string? Path { get; set; }

    public string? ProviderName { get; set; }

    public string? ResultMessage { get; set; }

    public bool? ResultSuccess { get; set; }

    public string? ResultStatus { get; set; }

    public string? ResponseText { get; set; }

    public int? ProviderRetryCount { get; set; }

    public string? SessionId { get; set; }

    public string? ShellCommand { get; set; }

    public int? ShellExitCode { get; set; }

    public string? TaskInput { get; set; }

    public int? TotalTokens { get; set; }

    public string? ToolCallId { get; set; }

    public string? ToolName { get; set; }

    public int? ToolRoundCount { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
