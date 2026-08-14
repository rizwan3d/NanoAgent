namespace StemCode.Application.Models;

public sealed class PermissionRequestDescriptor
{
    public PermissionRequestDescriptor(
        string toolName,
        string toolKind,
        IReadOnlyList<string> toolTags,
        IReadOnlyList<string> subjects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolKind);
        ArgumentNullException.ThrowIfNull(toolTags);
        ArgumentNullException.ThrowIfNull(subjects);

        ToolName = toolName.Trim();
        ToolKind = toolKind.Trim();
        ToolTags = toolTags
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Subjects = subjects
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> Subjects { get; }

    public string ToolKind { get; }

    public string ToolName { get; }

    public IReadOnlyList<string> ToolTags { get; }
}
