namespace StemCode.Domain.Models;

public sealed record ModelSelectionContext(
    IReadOnlyList<AvailableModel> AvailableModels,
    string? ConfiguredDefaultModel);
