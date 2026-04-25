namespace NanoAgent.Application.Models;

public sealed class MemorySettings
{
    public bool AllowAutoFailureObservation { get; set; } = true;

    public bool AllowAutoManualLessons { get; set; }

    public bool Disabled { get; set; }

    public int MaxEntries { get; set; } = 500;

    public int MaxPromptChars { get; set; } = 12_000;

    public bool RedactSecrets { get; set; } = true;

    public bool RequireApprovalForWrites { get; set; } = true;
}
