namespace NanoAgent.Application.Models;

public sealed class LifecycleHookSettings
{
    public bool Enabled { get; set; } = true;

    public int DefaultTimeoutSeconds { get; set; } = 30;

    public int MaxOutputCharacters { get; set; } = 12_000;

    public LifecycleHookRule[] Rules { get; set; } = [];
}

public sealed class LifecycleHookRule
{
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Event { get; set; }

    public string[] Events { get; set; } = [];

    public string? Command { get; set; }

    public string[] Arguments { get; set; } = [];

    public string? WorkingDirectory { get; set; }

    public bool RunInShell { get; set; } = true;

    public bool? ContinueOnError { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxOutputCharacters { get; set; }

    public string[] ToolNames { get; set; } = [];

    public string[] PathPatterns { get; set; } = [];

    public string[] ShellCommandPatterns { get; set; } = [];
}
