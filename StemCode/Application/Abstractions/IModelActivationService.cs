using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IModelActivationService
{
    ModelActivationResult Resolve(
        ReplSessionContext session,
        string requestedModel);
}
