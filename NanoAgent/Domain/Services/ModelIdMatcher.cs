namespace NanoAgent.Domain.Services;

internal static class ModelIdMatcher
{
    public static bool HasMatchingTerminalSegment(
        string modelId,
        string candidateModelId,
        StringComparison comparison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateModelId);

        int lastSlashIndex = modelId.LastIndexOf('/');
        if (lastSlashIndex < 0 || lastSlashIndex == modelId.Length - 1)
        {
            return false;
        }

        return string.Equals(
            modelId[(lastSlashIndex + 1)..],
            candidateModelId,
            comparison);
    }

    public static string? NormalizeOrNull(string? modelId)
    {
        string normalizedModelId = modelId?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedModelId)
            ? null
            : normalizedModelId;
    }
}
