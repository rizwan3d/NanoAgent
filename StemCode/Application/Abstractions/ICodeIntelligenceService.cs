using StemCode.Application.Tools.Models;

namespace StemCode.Application.Abstractions;

public interface ICodeIntelligenceService
{
    Task<CodeIntelligenceResult> QueryAsync(
        CodeIntelligenceRequest request,
        CancellationToken cancellationToken);
}
