namespace StemCode.Application.Models;

public sealed class WorkspaceFileEditTransaction
{
    public WorkspaceFileEditTransaction(
        string description,
        IReadOnlyList<WorkspaceFileEditState> beforeStates,
        IReadOnlyList<WorkspaceFileEditState> afterStates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(beforeStates);
        ArgumentNullException.ThrowIfNull(afterStates);

        Description = description.Trim();
        BeforeStates = beforeStates
            .Where(static state => state is not null)
            .ToArray();
        AfterStates = afterStates
            .Where(static state => state is not null)
            .ToArray();

        if (BeforeStates.Count == 0 && AfterStates.Count == 0)
        {
            throw new ArgumentException(
                "At least one before or after state must be provided.");
        }
    }

    public IReadOnlyList<WorkspaceFileEditState> AfterStates { get; }

    public IReadOnlyList<WorkspaceFileEditState> BeforeStates { get; }

    public string Description { get; }
}
