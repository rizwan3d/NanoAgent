namespace NanoAgent.CLI;

public abstract class UiModalState
{
    protected UiModalState(
        string title,
        string? description,
        bool allowCancellation,
        TimeSpan? autoSelectAfter,
        object completionToken)
    {
        Title = string.IsNullOrWhiteSpace(title)
            ? "Prompt"
            : title.Trim();
        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        AllowCancellation = allowCancellation;
        CompletionToken = completionToken;
        DeadlineUtc = autoSelectAfter.HasValue
            ? DateTimeOffset.UtcNow.Add(autoSelectAfter.Value)
            : null;
    }

    public bool AllowCancellation { get; }

    public object CompletionToken { get; }

    public string? Description { get; }

    public DateTimeOffset? DeadlineUtc { get; }

    public virtual int PanelSize => 12;

    public string Title { get; }

    public int GetRemainingSeconds()
    {
        if (DeadlineUtc is null)
        {
            return 0;
        }

        TimeSpan remaining = DeadlineUtc.Value - DateTimeOffset.UtcNow;
        return Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
    }

    public abstract string BuildBodyMarkup();

    public abstract string BuildFooterMarkup();

    public abstract string BuildInputMarkup();

    public abstract void HandleKey(AppState state, ConsoleKeyInfo key);

    public virtual void Update(AppState state)
    {
        if (DeadlineUtc is null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow < DeadlineUtc.Value)
        {
            return;
        }

        ResolveByTimeout(state);
    }

    protected abstract void ResolveByTimeout(AppState state);
}
