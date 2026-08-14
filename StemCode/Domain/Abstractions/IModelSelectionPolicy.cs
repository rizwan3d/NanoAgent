using StemCode.Domain.Models;

namespace StemCode.Domain.Abstractions;

public interface IModelSelectionPolicy
{
    ModelSelectionDecision Select(ModelSelectionContext context);
}
