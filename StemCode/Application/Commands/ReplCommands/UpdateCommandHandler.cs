using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.UI;

namespace StemCode.Application.Commands;

internal sealed class UpdateCommandHandler : IReplCommandHandler
{
    private readonly IApplicationUpdateService _updateService;
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly IStemCodeInstanceService _instanceService;

    public UpdateCommandHandler(
        IApplicationUpdateService updateService,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter,
        IStemCodeInstanceService instanceService)
    {
        _updateService = updateService;
        _confirmationPrompt = confirmationPrompt;
        _statusMessageWriter = statusMessageWriter;
        _instanceService = instanceService;
    }

    public string CommandName => "update";

    public string Description => "Check for StemCode updates and synchronize the matching Voice runtime.";

    public string Usage => "/update [now]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        bool installWithoutPrompt = string.Equals(
            context.ArgumentText?.Trim(),
            "now",
            StringComparison.OrdinalIgnoreCase);

        if (!installWithoutPrompt && !string.IsNullOrWhiteSpace(context.ArgumentText))
        {
            return ReplCommandResult.Continue(
                "Usage: /update [now]",
                ReplFeedbackKind.Error);
        }

        ApplicationUpdateInfo updateInfo;
        try
        {
            updateInfo = await _updateService.CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return ReplCommandResult.Continue(
                exception.Message,
                ReplFeedbackKind.Error);
        }

        // Plain `/update` remains a check-and-update command. `/update now` is also
        // a repair/synchronization path: even when the CLI is current, rerun the
        // matching release installer so StemCode.Voice is guaranteed to match it.
        if (!updateInfo.IsUpdateAvailable && !installWithoutPrompt)
        {
            return ReplCommandResult.Continue(
                $"StemCode is up to date. Current version: {updateInfo.CurrentVersion}.",
                ReplFeedbackKind.Info);
        }

        if (updateInfo.IsUpdateAvailable && !installWithoutPrompt)
        {
            bool shouldUpdate = await _confirmationPrompt.PromptAsync(
                new ConfirmationPromptRequest(
                    "A StemCode update is available. Update CLI and Voice runtime now?",
                    $"Current: {updateInfo.CurrentVersion}. Latest: {updateInfo.LatestVersion}. Choose Yes to update StemCode and its matching Voice runtime now, or No to skip.",
                    DefaultValue: false),
                cancellationToken);

            if (!shouldUpdate)
            {
                return ReplCommandResult.Continue(
                    $"Skipped StemCode {updateInfo.LatestVersion}. Release: {updateInfo.ReleaseUri}",
                    ReplFeedbackKind.Info);
            }
        }

        // Before replacing the running binary, terminate any other StemCode
        // sessions so the in-place update is not blocked by a file lock. In
        // 'now' (non-interactive) mode we terminate them automatically; in the
        // interactive flow we ask first.
        IReadOnlyList<RunningStemCodeInstance> otherInstances =
            await _instanceService.GetOtherRunningInstancesAsync(cancellationToken);

        if (otherInstances.Count > 0)
        {
            bool terminate = installWithoutPrompt;

            if (!terminate)
            {
                terminate = await _confirmationPrompt.PromptAsync(
                    new ConfirmationPromptRequest(
                        "Other StemCode sessions are running. Terminate them before updating?",
                        $"{otherInstances.Count} other StemCode session(s) are holding the running binary. " +
                        "Closing them lets the update replace the executable. Choose Yes to terminate them and continue, or No to attempt the update anyway.",
                        DefaultValue: true),
                    cancellationToken);
            }

            if (terminate)
            {
                await _statusMessageWriter.ShowInfoAsync(
                    $"Terminating {otherInstances.Count} other StemCode session(s)...",
                    cancellationToken);

                foreach (RunningStemCodeInstance instance in otherInstances)
                {
                    await _instanceService.TerminateAsync(instance, cancellationToken);
                }
            }
        }

        string installStatus = updateInfo.IsUpdateAvailable
            ? $"Installing StemCode {updateInfo.LatestVersion} and matching Voice runtime..."
            : $"Synchronizing StemCode {updateInfo.LatestVersion} and Voice runtime...";

        await _statusMessageWriter.ShowInfoAsync(
            installStatus,
            cancellationToken);

        ApplicationUpdateInstallResult installResult;
        try
        {
            installResult = await _updateService.InstallAsync(
                updateInfo,
                new StatusMessageProgress(_statusMessageWriter),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            HttpRequestException or
            PlatformNotSupportedException)
        {
            return ReplCommandResult.Continue(
                exception.Message,
                ReplFeedbackKind.Error);
        }

        return ReplCommandResult.Continue(
            installResult.Message,
            installResult.IsSuccess ? ReplFeedbackKind.Info : ReplFeedbackKind.Error);
    }
}
