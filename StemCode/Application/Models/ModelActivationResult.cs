namespace StemCode.Application.Models;

public sealed class ModelActivationResult
{
    public ModelActivationResult(
        ModelActivationStatus status,
        string? resolvedModelId,
        IReadOnlyList<string>? candidateModelIds = null)
    {
        Status = status;
        ResolvedModelId = resolvedModelId;
        CandidateModelIds = candidateModelIds ?? [];
    }

    public IReadOnlyList<string> CandidateModelIds { get; }

    public string? ResolvedModelId { get; }

    public ModelActivationStatus Status { get; }
}
