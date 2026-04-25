namespace NanoAgent.Application.Models;

public sealed class ToolAuditSettings
{
    public bool Enabled { get; set; }

    public int MaxArgumentsChars { get; set; } = 12_000;

    public int MaxResultChars { get; set; } = 12_000;

    public bool RedactSecrets { get; set; } = true;
}
