using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Permissions;

internal sealed class SelectionPermissionApprovalPrompt : IPermissionApprovalPrompt
{
    private readonly ISelectionPrompt _selectionPrompt;

    public SelectionPermissionApprovalPrompt(ISelectionPrompt selectionPrompt)
    {
        _selectionPrompt = selectionPrompt;
    }

    public Task<PermissionApprovalChoice> PromptAsync(
        PermissionApprovalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SelectionPromptRequest<PermissionApprovalChoice> selectionRequest = new(
            PermissionRequestDisplayFormatter.BuildApprovalTitle(request.Request),
            [
                new SelectionPromptOption<PermissionApprovalChoice>(
                    "Allow once",
                    PermissionApprovalChoice.AllowOnce,
                    "Run this request now without saving an override."),
                new SelectionPromptOption<PermissionApprovalChoice>(
                    $"Allow for {request.AgentName}",
                    PermissionApprovalChoice.AllowForAgent,
                    "Remember an allow override for this exact pattern on the current agent."),
                new SelectionPromptOption<PermissionApprovalChoice>(
                    "Deny once",
                    PermissionApprovalChoice.DenyOnce,
                    "Block this request now but keep prompting in the future."),
                new SelectionPromptOption<PermissionApprovalChoice>(
                    $"Deny for {request.AgentName}",
                    PermissionApprovalChoice.DenyForAgent,
                    "Remember a deny override for this exact pattern on the current agent.")
            ],
            PermissionRequestDisplayFormatter.BuildPromptDescription(request),
            DefaultIndex: 0,
            AllowCancellation: true,
            AutoSelectAfter: TimeSpan.FromSeconds(10));

        return _selectionPrompt.PromptAsync(selectionRequest, cancellationToken);
    }
}
