namespace StemCode.Application.Tools.Models;

public sealed record PlanningModeResult(
    string Objective,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<string> SuggestedResponseSections);
