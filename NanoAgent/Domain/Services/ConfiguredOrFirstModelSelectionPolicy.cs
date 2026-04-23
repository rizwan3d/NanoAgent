using NanoAgent.Application.Exceptions;
using NanoAgent.Domain.Abstractions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Domain.Services;

internal sealed class ConfiguredOrFirstModelSelectionPolicy : IModelSelectionPolicy
{
    public ModelSelectionDecision Select(ModelSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AvailableModels.Count == 0)
        {
            throw new ModelSelectionException(
                "Model selection cannot run because the provider returned no available models.");
        }

        string? configuredDefaultModel = ModelIdMatcher.NormalizeOrNull(context.ConfiguredDefaultModel);
        string? matchedConfiguredDefaultModel = ResolvePreferredModelId(
            context.AvailableModels,
            configuredDefaultModel);

        if (matchedConfiguredDefaultModel is not null)
        {
            return new ModelSelectionDecision(
                matchedConfiguredDefaultModel,
                ModelSelectionSource.ConfiguredDefault,
                ConfiguredDefaultModelStatus.Matched,
                configuredDefaultModel);
        }

        AvailableModel firstReturnedModel = context.AvailableModels[0];

        return new ModelSelectionDecision(
            firstReturnedModel.Id,
            ModelSelectionSource.FirstReturnedModel,
            configuredDefaultModel is null
                ? ConfiguredDefaultModelStatus.NotConfigured
                : ConfiguredDefaultModelStatus.NotFound,
            configuredDefaultModel);
    }

    private static string? ResolvePreferredModelId(
        IReadOnlyList<AvailableModel> availableModels,
        string? preferredModelId)
    {
        string? normalizedPreferredModelId = ModelIdMatcher.NormalizeOrNull(preferredModelId);
        if (normalizedPreferredModelId is null)
        {
            return null;
        }

        foreach (AvailableModel availableModel in availableModels)
        {
            if (string.Equals(availableModel.Id, normalizedPreferredModelId, StringComparison.Ordinal))
            {
                return availableModel.Id;
            }
        }

        foreach (AvailableModel availableModel in availableModels)
        {
            if (ModelIdMatcher.HasMatchingTerminalSegment(
                    availableModel.Id,
                    normalizedPreferredModelId,
                    StringComparison.Ordinal))
            {
                return availableModel.Id;
            }
        }

        return null;
    }
}
