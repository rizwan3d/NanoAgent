namespace NanoAgent.Desktop.Models;

public sealed record WorkspaceSectionInfo(
    string SectionId,
    string Title,
    DateTimeOffset UpdatedAtUtc,
    int TurnCount,
    string ActiveModelId,
    string WorkspacePath)
{
    public string Subtitle => $"{TurnCount} {(TurnCount == 1 ? "turn" : "turns")} - {ActiveModelId}";

    public string UpdatedText => UpdatedAtUtc.ToLocalTime().ToString("MMM d, h:mm tt");
}
