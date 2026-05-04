using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ConfigCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IUserDataPathProvider _userDataPathProvider;

    public ConfigCommandHandler(
        IAgentConfigurationStore configurationStore,
        IUserDataPathProvider userDataPathProvider)
    {
        _configurationStore = configurationStore;
        _userDataPathProvider = userDataPathProvider;
    }

    public string CommandName => "config";

    public string Description => "Show provider, config-path, active-profile, thinking, and active-model details.";

    public string Usage => "/config";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string baseUrl = context.Session.ProviderProfile.ProviderKind.GetManagedBaseUrl()
            ?? context.Session.ProviderProfile.BaseUrl
            ?? "(not configured)";
        AgentConfiguration? configuration = await _configurationStore.LoadAsync(cancellationToken);
        string savedProvider = string.IsNullOrWhiteSpace(configuration?.ActiveProviderName)
            ? "(legacy/default)"
            : configuration.ActiveProviderName;

        string message =
            "Current configuration:\n" +
            $"Session: {context.Session.SessionId}\n" +
            $"Resume command: {context.Session.SectionResumeCommand}\n" +
            $"Saved provider: {savedProvider}\n" +
            $"Provider: {context.Session.ProviderName}\n" +
            $"Base URL: {baseUrl}\n" +
            $"Configuration file: {_userDataPathProvider.GetConfigurationFilePath()}\n" +
            $"MCP configuration: agent-profile.json mcpServers\n" +
            $"Agent profile: {context.Session.AgentProfile.Name} - {context.Session.AgentProfile.Description}\n" +
            $"Thinking: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}\n" +
            $"Active model: {context.Session.ActiveModelId}";

        return ReplCommandResult.Continue(message);
    }
}
