namespace StemCode.Application.Models;

public sealed class PatchPermissionPolicy
{
    public string[] AllowedRoots { get; set; } = [];

    public ToolPathAccessKind Kind { get; set; } = ToolPathAccessKind.Write;

    public string PatchArgumentName { get; set; } = "patch";
}
