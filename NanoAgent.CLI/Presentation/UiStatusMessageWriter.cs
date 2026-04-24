using NanoAgent.Application.Abstractions;

namespace NanoAgent.CLI;

public sealed class UiStatusMessageWriter : IStatusMessageWriter
{
    private const string ExistingProviderConfigurationPrefix = "Using existing provider configuration:";
    private readonly IUiBridge _uiBridge;

    public UiStatusMessageWriter(IUiBridge uiBridge)
    {
        _uiBridge = uiBridge;
    }

    public Task ShowErrorAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _uiBridge.ShowError(message);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message.StartsWith(ExistingProviderConfigurationPrefix, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _uiBridge.ShowInfo(message);
        return Task.CompletedTask;
    }

    public Task ShowSuccessAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _uiBridge.ShowSuccess(message);
        return Task.CompletedTask;
    }
}
