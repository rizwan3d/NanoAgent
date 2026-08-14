using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.UI;

namespace StemCode.Application.Commands;

internal sealed class UpdateCommandHandler : IReplCommandHandler
{
    private readonly IApplicationUpdateService _updateService;
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly IStatusMessageWriter _statusMessageWriter;

    public UpdateCommandHandler(
        IApplicationUpdateService updateService,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter)
    {
        _updateService = updateService;
        _confirmationPrompt = confirmationPrompt;
        _statusMessageWriter = statusMessageWriter;
    }

    public string CommandName => "update";

    public string Description => "Check for StemCode updates and install the latest release.";

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

        if (!updateInfo.IsUpdateAvailable)
        {
            return ReplCommandResult.Continue(
                $"StemCode is up to date. Current version: {updateInfo.CurrentVersion}.",
                ReplFeedbackKind.Info);
        }

        if (!installWithoutPrompt)
        {
            bool shouldUpdate = await _confirmationPrompt.PromptAsync(
                new ConfirmationPromptRequest(
                    "A StemCode update is available. Update now?",
                    $"Current: {updateInfo.CurrentVersion}. Latest: {updateInfo.LatestVersion}. Choose Yes to update now, or No to skip.",
                    DefaultValue: false),
                cancellationToken);

            if (!shouldUpdate)
            {
                return ReplCommandResult.Continue(
                    $"Skipped StemCode {updateInfo.LatestVersion}. Release: {updateInfo.ReleaseUri}",
                    ReplFeedbackKind.Info);
            }
        }

        await _statusMessageWriter.ShowInfoAsync(
            $"Installing StemCode {updateInfo.LatestVersion}...",
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
