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

        bool requiresSetup;
        try
        {
            requiresSetup = _bootstrapper.RequiresSetup();
        }
        catch (Exception exception)
        {
            await _statusMessageWriter.ShowErrorAsync(
                "Windows sandbox setup check failed: " + exception.Message,
                cancellationToken);
            return;
        }

        if (!requiresSetup)
        {
            return;
        }

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
            return;
        }

        if (!shouldSetup)
        {
            await _statusMessageWriter.ShowInfoAsync(SetupSkippedMessage, cancellationToken);
            return;
        }

        await _statusMessageWriter.ShowInfoAsync(SetupStartingMessage, cancellationToken);

        try
        {
            _bootstrapper.EnsureSetup();
            await _statusMessageWriter.ShowSuccessAsync(SetupSuccessMessage, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await _statusMessageWriter.ShowInfoAsync(SetupCanceledMessage, cancellationToken);
        }
        catch (Exception exception)
        {
            await _statusMessageWriter.ShowErrorAsync(
                "Windows sandbox setup failed: " + exception.Message,
                cancellationToken);
        }
    }
}
