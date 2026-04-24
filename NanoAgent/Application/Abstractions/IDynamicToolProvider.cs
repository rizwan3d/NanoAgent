namespace NanoAgent.Application.Abstractions;

public interface IDynamicToolProvider
{
    IReadOnlyList<ITool> GetTools();

    IReadOnlyList<DynamicToolProviderStatus> GetStatuses();
}

public sealed record DynamicToolProviderStatus(
    string Name,
    string Kind,
    bool Enabled,
    bool IsAvailable,
    int ToolCount,
    string? Details);
