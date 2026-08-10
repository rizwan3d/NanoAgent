namespace StemCode.Application.Models;

public sealed class FilePathPermissionRule
{
    public string[] AllowedRoots { get; set; } = [];

    public string ArgumentName { get; set; } = string.Empty;

    public ToolPathAccessKind Kind { get; set; } = ToolPathAccessKind.Read;
}
