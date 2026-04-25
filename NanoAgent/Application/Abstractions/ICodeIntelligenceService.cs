using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface ICodeIntelligenceService
{
    Task<CodeIntelligenceResult> QueryAsync(
        CodeIntelligenceRequest request,
        CancellationToken cancellationToken);
}
