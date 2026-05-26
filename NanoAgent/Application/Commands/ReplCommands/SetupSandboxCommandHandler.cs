using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class SetupSandboxCommandHandler : IReplCommandHandler
{
    private readonly IWindowsSandboxStartupService _windowsSandboxStartupService;

    public SetupSandboxCommandHandler(IWindowsSandboxStartupService windowsSandboxStartupService)
    {
        _windowsSandboxStartupService = windowsSandboxStartupService;
    }

    public string CommandName => "setup-sandbox";

    public string Description => "Set up Windows sandbox support for restricted shell commands.";

    public string Usage => "/setup-sandbox";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Arguments.Count > 0)
        {
            return ReplCommandResult.Continue(
                "Usage: /setup-sandbox",
                ReplFeedbackKind.Error);
        }

        WindowsSandboxSetupResult result = await _windowsSandboxStartupService.SetupAsync(cancellationToken);
        return ReplCommandResult.Continue(
            result.Message,
            result.State switch
            {
                WindowsSandboxSetupState.Failed => ReplFeedbackKind.Error,
                WindowsSandboxSetupState.Canceled or WindowsSandboxSetupState.Skipped => ReplFeedbackKind.Warning,
                _ => ReplFeedbackKind.Info
            });
    }
}
