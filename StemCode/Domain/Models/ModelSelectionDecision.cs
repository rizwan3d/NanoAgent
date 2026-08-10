namespace StemCode.Domain.Models;

public sealed record ModelSelectionDecision(
    string SelectedModelId,
    ModelSelectionSource SelectionSource,
    ConfiguredDefaultModelStatus ConfiguredDefaultStatus,
    string? ConfiguredDefaultModel);
