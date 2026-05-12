namespace NanoAgent.Application.Models;

/// <summary>
/// Top-level session record that can contain multiple sections
/// and holds accumulated context from completed sections.
/// </summary>
public sealed class SessionRecord
{
    public SessionRecord(
        string sessionId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<string> sectionIds,
        SessionContext accumulatedContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!Guid.TryParse(sessionId.Trim(), out Guid _))
        {
            throw new ArgumentException("Session id must be a valid GUID.", nameof(sessionId));
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
        }

        SessionId = sessionId.Trim();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        SectionIds = sectionIds ?? [];
        AccumulatedContext = accumulatedContext ?? SessionContext.Empty;
    }

    /// <summary>
    /// Creates a new session with a single initial section.
    /// </summary>
    public static SessionRecord CreateWithSection(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new SessionRecord(
            Guid.NewGuid().ToString("D"),
            now,
            now,
            [sectionId],
            SessionContext.Empty);
    }

    public string SessionId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>
    /// Ordered list of section IDs belonging to this session.
    /// The last one is the active/completed section.
    /// </summary>
    public IReadOnlyList<string> SectionIds { get; }

    /// <summary>
    /// Accumulated context from completed sections.
    /// </summary>
    public SessionContext AccumulatedContext { get; }

    public SessionRecord WithNewSection(
        string sectionId,
        SessionContext sectionContext,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentNullException.ThrowIfNull(sectionContext);

        List<string> updatedSectionIds = new(SectionIds) { sectionId };

        return new SessionRecord(
            SessionId,
            CreatedAtUtc,
            now,
            updatedSectionIds,
            AccumulatedContext.Merge(sectionContext));
    }

    public SessionRecord WithUpdatedTimestamp(DateTimeOffset now)
    {
        return new SessionRecord(
            SessionId,
            CreatedAtUtc,
            now,
            SectionIds,
            AccumulatedContext);
    }
}
