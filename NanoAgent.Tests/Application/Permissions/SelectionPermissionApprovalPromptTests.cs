using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;

namespace NanoAgent.Tests.Application.Permissions;

public sealed class SelectionPermissionApprovalPromptTests
{
    [Fact]
    public async Task PromptAsync_Should_ShowExactFilePath_ForFileWriteRequests()
    {
        CapturingSelectionPrompt selectionPrompt = new(PermissionApprovalChoice.AllowOnce);
        SelectionPermissionApprovalPrompt sut = new(selectionPrompt);

        await sut.PromptAsync(
            new PermissionApprovalRequest(
                "NanoAgent",
                new PermissionRequestDescriptor(
                    "file_write",
                    "edit",
                    ["edit", "file_write"],
                    ["src/App.js"]),
                "Permission requires approval for tool 'file_write' to write file 'src/App.js'."),
            CancellationToken.None);

        selectionPrompt.LastRequest!.Title.Should().Be("Approve file write?");
        selectionPrompt.LastRequest.DefaultIndex.Should().Be(0);
        selectionPrompt.LastRequest.AutoSelectAfter.Should().Be(TimeSpan.FromSeconds(10));
        selectionPrompt.LastRequest.Description.Should().Contain("Tool: file_write");
        selectionPrompt.LastRequest.Description.Should().Contain("File path: src/App.js");
    }

    [Fact]
    public async Task PromptAsync_Should_ShowExactPatchTargets_ForApplyPatchRequests()
    {
        CapturingSelectionPrompt selectionPrompt = new(PermissionApprovalChoice.AllowOnce);
        SelectionPermissionApprovalPrompt sut = new(selectionPrompt);

        await sut.PromptAsync(
            new PermissionApprovalRequest(
                "NanoAgent",
                new PermissionRequestDescriptor(
                    "apply_patch",
                    "edit",
                    ["edit", "apply_patch"],
                    ["src/App.js", "src/styles.css"]),
                "Permission requires approval for tool 'apply_patch' to modify patch targets."),
            CancellationToken.None);

        selectionPrompt.LastRequest!.Title.Should().Be("Approve patch changes?");
        selectionPrompt.LastRequest.Description.Should().Contain("Patch targets:");
        selectionPrompt.LastRequest.Description.Should().Contain("- src/App.js");
        selectionPrompt.LastRequest.Description.Should().Contain("- src/styles.css");
    }

    [Fact]
    public async Task PromptAsync_Should_ShowExactFilePath_ForFileDeleteRequests()
    {
        CapturingSelectionPrompt selectionPrompt = new(PermissionApprovalChoice.AllowOnce);
        SelectionPermissionApprovalPrompt sut = new(selectionPrompt);

        await sut.PromptAsync(
            new PermissionApprovalRequest(
                "NanoAgent",
                new PermissionRequestDescriptor(
                    "file_delete",
                    "edit",
                    ["edit", "file_delete"],
                    ["src/App.js"]),
                "Permission requires approval for tool 'file_delete' to delete file 'src/App.js'."),
            CancellationToken.None);

        selectionPrompt.LastRequest!.Title.Should().Be("Approve file delete?");
        selectionPrompt.LastRequest.Description.Should().Contain("Tool: file_delete");
        selectionPrompt.LastRequest.Description.Should().Contain("File path: src/App.js");
    }

    [Fact]
    public async Task PromptAsync_Should_ShowExactCommand_ForShellRequests()
    {
        CapturingSelectionPrompt selectionPrompt = new(PermissionApprovalChoice.AllowOnce);
        SelectionPermissionApprovalPrompt sut = new(selectionPrompt);

        await sut.PromptAsync(
            new PermissionApprovalRequest(
                "NanoAgent",
                new PermissionRequestDescriptor(
                    "shell_command",
                    "bash",
                    ["bash", "shell_command"],
                    ["git status --short"]),
                "Permission requires approval for tool 'shell_command' to run command 'git status --short'."),
            CancellationToken.None);

        selectionPrompt.LastRequest!.Title.Should().Be("Approve shell command?");
        selectionPrompt.LastRequest.Description.Should().Contain("Command: git status --short");
    }

    private sealed class CapturingSelectionPrompt : ISelectionPrompt
    {
        private readonly PermissionApprovalChoice _choice;

        public CapturingSelectionPrompt(PermissionApprovalChoice choice)
        {
            _choice = choice;
        }

        public SelectionPromptRequest<PermissionApprovalChoice>? LastRequest { get; private set; }

        public Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = (SelectionPromptRequest<PermissionApprovalChoice>)(object)request;
            return Task.FromResult((T)(object)_choice);
        }
    }
}
