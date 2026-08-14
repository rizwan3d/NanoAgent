using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface ITool
{
    string Description { get; }

    string Name { get; }

    string PermissionRequirements { get; }

    string Schema { get; }

    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}
