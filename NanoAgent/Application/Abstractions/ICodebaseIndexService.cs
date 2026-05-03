using NanoAgent.Application.Tools.Models;

namespace NanoAgent.Application.Abstractions;

public interface ICodebaseIndexService
{
    Task<CodebaseIndexBuildResult> BuildAsync(
        bool force,
        CancellationToken cancellationToken);

    Task<CodebaseIndexStatusResult> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<CodebaseIndexSearchResult> SearchAsync(
        string query,
        int limit,
        bool includeSnippets,
        CancellationToken cancellationToken);

    Task<CodebaseIndexListResult> ListAsync(
        int limit,
        CancellationToken cancellationToken);
}
