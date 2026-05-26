using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.WindowsSandbox;

internal interface IWindowsSandboxSetupBootstrapper
{
    bool RequiresSetup();

    void EnsureSetup();
}

internal sealed class WindowsSandboxSetupBootstrapper : IWindowsSandboxSetupBootstrapper
{
    public bool RequiresSetup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return !WindowsSandboxSetupOrchestrator.IsSetupFresh(CreatePayload());
    }

    public void EnsureSetup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsSandboxSetupOrchestrator.RunSetup(
            CreatePayload(),
            needsElevation: !WindowsSandboxSetupOrchestrator.IsElevated());
    }

    private static WindowsSandboxSetupPayload CreatePayload()
    {
        return new WindowsSandboxSetupPayload
        {
            NanoAgentHome = WindowsSandboxPaths.ResolveAppHome(),
            CommandCwd = Directory.GetCurrentDirectory(),
            RealUser = Environment.UserName,
            SandboxUsernames =
            [
                WindowsSandboxPaths.OfflineUsername,
                WindowsSandboxPaths.OnlineUsername
            ]
        };
    }
}

internal sealed class WindowsSandboxStartupService : IWindowsSandboxStartupService
{
    private const string PromptTitle = "Windows sandbox setup required. Set it up now?";
    private const string PromptDescription =
        "NanoAgent uses Windows sandbox accounts to run restricted shell commands safely. " +
        "Setup only runs once and may ask for administrator approval.";
    private const string SetupStartingMessage =
        "Setting up Windows sandbox. Windows may ask for administrator approval.";
    private const string SetupSkippedMessage =
        "Skipped Windows sandbox setup. Restricted Windows shell commands may fail until setup is completed.";
    private const string SetupCanceledMessage =
        "Windows sandbox setup was canceled. Restricted Windows shell commands may fail until setup is completed.";
    private const string SetupSuccessMessage = "Windows sandbox setup is ready.";
    private const string SetupAlreadyReadyMessage = "Windows sandbox setup is already ready.";

    private readonly BackendRuntimeOptions _runtimeOptions;
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly IStatusMessageWriter _statusMessageWriter;
    private readonly IWindowsSandboxSetupBootstrapper _bootstrapper;

    public WindowsSandboxStartupService(
        BackendRuntimeOptions runtimeOptions,
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter,
        IWindowsSandboxSetupBootstrapper bootstrapper)
    {
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
        _confirmationPrompt = confirmationPrompt ?? throw new ArgumentNullException(nameof(confirmationPrompt));
        _statusMessageWriter = statusMessageWriter ?? throw new ArgumentNullException(nameof(statusMessageWriter));
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!_runtimeOptions.EnableStartupPrompts)
        {
            return;
        }

        await RunSetupAsync(
            promptForConfirmation: true,
            announceAlreadyReady: false,
            announceUnsupported: false,
            cancellationToken);
    }

    public Task<WindowsSandboxSetupResult> SetupAsync(CancellationToken cancellationToken)
    {
        return RunSetupAsync(
            promptForConfirmation: false,
            announceAlreadyReady: true,
            announceUnsupported: true,
            cancellationToken);
    }

    private async Task<WindowsSandboxSetupResult> RunSetupAsync(
        bool promptForConfirmation,
        bool announceAlreadyReady,
        bool announceUnsupported,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            const string message = "Windows sandbox setup is only required on Windows.";
            if (announceUnsupported)
            {
                await _statusMessageWriter.ShowInfoAsync(message, cancellationToken);
            }

            return new WindowsSandboxSetupResult(WindowsSandboxSetupState.NotSupported, message);
        }

        bool requiresSetup;
        try
        {
            requiresSetup = _bootstrapper.RequiresSetup();
        }
        catch (Exception exception)
        {
            string message = "Windows sandbox setup check failed: " + exception.Message;
            await _statusMessageWriter.ShowErrorAsync(
                message,
                cancellationToken);
            return new WindowsSandboxSetupResult(WindowsSandboxSetupState.Failed, message);
        }

        if (!requiresSetup)
        {
            if (announceAlreadyReady)
            {
                await _statusMessageWriter.ShowInfoAsync(
                    SetupAlreadyReadyMessage,
                    cancellationToken);
            }

            return new WindowsSandboxSetupResult(
                WindowsSandboxSetupState.AlreadyReady,
                SetupAlreadyReadyMessage);
        }

        if (promptForConfirmation)
        {
            bool shouldSetup;
            try
            {
                shouldSetup = await _confirmationPrompt.PromptAsync(
                    new ConfirmationPromptRequest(
                        PromptTitle,
                        PromptDescription,
                        DefaultValue: true),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await _statusMessageWriter.ShowInfoAsync(SetupSkippedMessage, cancellationToken);
                return new WindowsSandboxSetupResult(WindowsSandboxSetupState.Skipped, SetupSkippedMessage);
            }

            if (!shouldSetup)
            {
                await _statusMessageWriter.ShowInfoAsync(SetupSkippedMessage, cancellationToken);
                return new WindowsSandboxSetupResult(WindowsSandboxSetupState.Skipped, SetupSkippedMessage);
            }
        }

        await _statusMessageWriter.ShowInfoAsync(SetupStartingMessage, cancellationToken);

        try
        {
            _bootstrapper.EnsureSetup();
            await _statusMessageWriter.ShowSuccessAsync(SetupSuccessMessage, cancellationToken);
            return new WindowsSandboxSetupResult(WindowsSandboxSetupState.Ready, SetupSuccessMessage);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await _statusMessageWriter.ShowInfoAsync(SetupCanceledMessage, cancellationToken);
            return new WindowsSandboxSetupResult(WindowsSandboxSetupState.Canceled, SetupCanceledMessage);
        }
        catch (Exception exception)
        {
            string message = "Windows sandbox setup failed: " + exception.Message;
            await _statusMessageWriter.ShowErrorAsync(
                message,
                cancellationToken);
            return new WindowsSandboxSetupResult(WindowsSandboxSetupState.Failed, message);
        }
    }
}
