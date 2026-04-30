using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Application.Commands;

internal sealed class ConfigCommandHandler : IReplCommandHandler
{
    private readonly IUserDataPathProvider _userDataPathProvider;

    public ConfigCommandHandler(IUserDataPathProvider userDataPathProvider)
    {
        _userDataPathProvider = userDataPathProvider;
    }

    public string CommandName => "config";

    public string Description => "Show provider, config-path, active-profile, thinking, and active-model details.";

    public string Usage => "/config";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string baseUrl = context.Session.ProviderProfile.ProviderKind.GetManagedBaseUrl()
            ?? context.Session.ProviderProfile.BaseUrl
            ?? "(not configured)";

        string message =
            "Current configuration:\n" +
            $"Session: {context.Session.SessionId}\n" +
            $"Resume command: {context.Session.SectionResumeCommand}\n" +
            $"Provider: {context.Session.ProviderName}\n" +
            $"Base URL: {baseUrl}\n" +
            $"Configuration file: {_userDataPathProvider.GetConfigurationFilePath()}\n" +
            $"MCP configuration: agent-profile.json mcpServers\n" +
            $"Agent profile: {context.Session.AgentProfile.Name} - {context.Session.AgentProfile.Description}\n" +
            $"Thinking: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}\n" +
            $"Active model: {context.Session.ActiveModelId}";

        return Task.FromResult(ReplCommandResult.Continue(message));
    }
}
