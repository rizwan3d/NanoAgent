namespace StemCode.Application.Models;

public sealed record PermissionApprovalRequest(
    string AgentName,
    PermissionRequestDescriptor Request,
    string Reason);
