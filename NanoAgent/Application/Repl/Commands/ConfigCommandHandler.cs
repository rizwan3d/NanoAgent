using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Repl.Commands;

internal sealed class ConfigCommandHandler : IReplCommandHandler
{
    private const string OpenAiBaseUrl = "https://api.openai.com/v1/";

    private readonly IUserDataPathProvider _userDataPathProvider;

    public ConfigCommandHandler(IUserDataPathProvider userDataPathProvider)
    {
        _userDataPathProvider = userDataPathProvider;
    }

    public string CommandName => "config";

    public string Description => "Show provider, config-path, and active-model details.";

    public string Usage => "/config";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string baseUrl = context.Session.ProviderProfile.ProviderKind == Domain.Models.ProviderKind.OpenAi
            ? OpenAiBaseUrl
            : context.Session.ProviderProfile.BaseUrl ?? "(not configured)";

        string message =
            "Current configuration:\n" +
            $"Provider: {context.Session.ProviderName}\n" +
            $"Base URL: {baseUrl}\n" +
            $"Configuration file: {_userDataPathProvider.GetConfigurationFilePath()}\n" +
            $"Agent profile: {context.Session.AgentProfile.Name} - {context.Session.AgentProfile.Description}\n" +
            $"Active model: {context.Session.ActiveModelId}";

        return Task.FromResult(ReplCommandResult.Continue(message));
    }
}
