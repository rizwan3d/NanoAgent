namespace StemCode.Application.Models;

public sealed class ToolPermissionPolicy
{
    public ToolApprovalMode ApprovalMode { get; set; } = ToolApprovalMode.Automatic;

    public bool BypassUserPermissionRules { get; set; }

    public FilePathPermissionRule[] FilePaths { get; set; } = [];

    public PatchPermissionPolicy? Patch { get; set; }

    public ShellCommandPermissionPolicy? Shell { get; set; }

    public string[] ToolTags { get; set; } = [];

    public WebRequestPermissionPolicy? WebRequest { get; set; }
}
