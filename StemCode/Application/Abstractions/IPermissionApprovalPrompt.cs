using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IPermissionApprovalPrompt
{
    Task<PermissionApprovalChoice> PromptAsync(
        PermissionApprovalRequest request,
        CancellationToken cancellationToken);
}
