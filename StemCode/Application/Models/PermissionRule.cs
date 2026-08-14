namespace StemCode.Application.Models;

public sealed class PermissionRule
{
    public PermissionMode Mode { get; set; } = PermissionMode.Ask;

    public string[] Patterns { get; set; } = [];

    public string[] Tools { get; set; } = [];
}
