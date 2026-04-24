using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.DependencyInjection;
using NanoAgent.Application.Models;
using NanoAgent.CLI.Commands;
using NanoAgent.Infrastructure.DependencyInjection;

namespace NanoAgent.CLI;

public sealed class NanoCliBackend : INanoAgentBackend
{
    private readonly string[] _args;
    private IAgentTurnService? _agentTurnService;
    private IHost? _host;
    private IFirstRunOnboardingService? _onboardingService;
    private IModelDiscoveryService? _modelDiscoveryService;
    private IReplCommandDispatcher? _commandDispatcher;
    private IReplCommandParser? _commandParser;
    private ReplSessionContext? _session;
    private ISessionAppService? _sessionAppService;

    public NanoCliBackend(string[] args)
    {
        _args = args;
    }

    public async Task<BackendSessionInfo> InitializeAsync(
        IUiBridge uiBridge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uiBridge);

        if (_session is not null)
        {
            return CreateSessionInfo(_session);
        }

        CliSessionOptions options = ParseSessionOptions(_args);

        _host = CreateHost(uiBridge, _args);
        _onboardingService = _host.Services.GetRequiredService<IFirstRunOnboardingService>();
        _modelDiscoveryService = _host.Services.GetRequiredService<IModelDiscoveryService>();
        _sessionAppService = _host.Services.GetRequiredService<ISessionAppService>();
        _agentTurnService = _host.Services.GetRequiredService<IAgentTurnService>();
        _commandParser = _host.Services.GetRequiredService<IReplCommandParser>();
        _commandDispatcher = _host.Services.GetRequiredService<IReplCommandDispatcher>();

        if (!string.IsNullOrWhiteSpace(options.SectionId))
        {
            _session = await _sessionAppService.ResumeAsync(
                new ResumeSessionRequest(
                    options.SectionId,
                    options.ProfileName,
                    options.ThinkingMode),
                cancellationToken);

            await _onboardingService.EnsureOnboardedAsync(cancellationToken);
        }
        else
        {
            OnboardingResult onboardingResult = await _onboardingService.EnsureOnboardedAsync(cancellationToken);
            ModelDiscoveryResult modelResult = await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken);
            string? reasoningEffort = options.ThinkingMode ?? onboardingResult.ReasoningEffort;

            _session = await _sessionAppService.CreateAsync(
                new CreateSessionRequest(
                    onboardingResult.Profile,
                    modelResult.SelectedModelId,
                    modelResult.AvailableModels.Select(static model => model.Id).ToArray(),
                    options.ProfileName,
                    reasoningEffort),
                cancellationToken);
        }

        return CreateSessionInfo(_session);
    }

    public async Task<BackendCommandResult> RunCommandAsync(
        string commandText,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        if (_session is null ||
            _sessionAppService is null ||
            _commandParser is null ||
            _commandDispatcher is null)
        {
            throw new InvalidOperationException("NanoAgent backend has not been initialized.");
        }

        ParsedReplCommand command = _commandParser.Parse(commandText);
        ReplCommandResult result = await _commandDispatcher.DispatchAsync(
            command,
            _session,
            cancellationToken);

        await _sessionAppService.SaveIfDirtyAsync(_session, cancellationToken);

        return new BackendCommandResult(
            result,
            CreateSessionInfo(_session));
    }

    public async Task<ConversationTurnResult> RunTurnAsync(
        string input,
        IUiBridge uiBridge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(uiBridge);

        if (_session is null ||
            _sessionAppService is null ||
            _agentTurnService is null)
        {
            throw new InvalidOperationException("NanoAgent backend has not been initialized.");
        }

        _sessionAppService.EnsureTitleGenerationStarted(_session, input);

        ConversationTurnResult result = await _agentTurnService.RunTurnAsync(
            new AgentTurnRequest(
                _session,
                input,
                new NanoCliProgressSink(uiBridge)),
            cancellationToken);

        ConversationTurnMetrics? metrics = result.Metrics;
        if (!string.IsNullOrWhiteSpace(result.ResponseText) && metrics is not null)
        {
            int sessionTotal = _session.AddEstimatedOutputTokens(metrics.EstimatedOutputTokens);
            metrics = metrics.WithSessionEstimatedOutputTokens(sessionTotal);
        }

        await _sessionAppService.SaveIfDirtyAsync(_session, cancellationToken);

        return new ConversationTurnResult(
            result.Kind,
            result.ResponseText,
            result.ToolExecutionResult,
            metrics);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sessionAppService is not null && _session is not null)
        {
            try
            {
                await _sessionAppService.StopAsync(_session, CancellationToken.None);
            }
            catch
            {
            }
        }

        if (_host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _host?.Dispose();
        }
    }

    private static IHost CreateHost(IUiBridge uiBridge, string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: false);

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        builder.Services.AddSingleton(uiBridge);
        builder.Services
            .AddApplication()
            .AddNanoCliCommands()
            .AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton<ISelectionPrompt, UiSelectionPrompt>();
        builder.Services.AddSingleton<ITextPrompt, UiTextPrompt>();
        builder.Services.AddSingleton<ISecretPrompt, UiSecretPrompt>();
        builder.Services.AddSingleton<IConfirmationPrompt, UiConfirmationPrompt>();
        builder.Services.AddSingleton<IStatusMessageWriter, UiStatusMessageWriter>();

        return builder.Build();
    }

    private static BackendSessionInfo CreateSessionInfo(ReplSessionContext session)
    {
        return new BackendSessionInfo(
            session.SessionId,
            session.SectionResumeCommand,
            session.ProviderName,
            session.ActiveModelId,
            session.AvailableModelIds,
            ReasoningEffortOptions.Format(session.ReasoningEffort),
            session.AgentProfileName,
            session.SectionTitle,
            session.IsResumedSection,
            CreateConversationHistory(session));
    }

    private static IReadOnlyList<BackendConversationMessage> CreateConversationHistory(
        ReplSessionContext session)
    {
        return session.ConversationHistory
            .Where(static message =>
                !string.IsNullOrWhiteSpace(message.Content) &&
                (string.Equals(message.Role, "user", StringComparison.Ordinal) ||
                    string.Equals(message.Role, "assistant", StringComparison.Ordinal)))
            .Select(static message => new BackendConversationMessage(
                message.Role,
                message.Content!))
            .ToArray();
    }

    private static CliSessionOptions ParseSessionOptions(IReadOnlyList<string> args)
    {
        string? sectionId = null;
        string? profileName = null;
        string? thinkingMode = null;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];

            if (TryReadOptionValue(args, ref index, "--section", out string? sectionValue) ||
                TryReadOptionValue(args, ref index, "--session", out sectionValue))
            {
                sectionId = sectionValue;
                continue;
            }

            if (TryReadOptionValue(args, ref index, "--profile", out string? profileValue))
            {
                profileName = profileValue;
                continue;
            }

            if (TryReadOptionValue(args, ref index, "--thinking", out string? thinkingValue))
            {
                thinkingMode = thinkingValue;
            }
        }

        return new CliSessionOptions(sectionId, profileName, thinkingMode);
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string? value)
    {
        string arg = args[index];
        value = null;

        if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
        {
            int valueIndex = index + 1;
            if (valueIndex >= args.Count || string.IsNullOrWhiteSpace(args[valueIndex]))
            {
                throw new ArgumentException($"Missing value for {optionName}.");
            }

            value = args[valueIndex].Trim();
            index = valueIndex;
            return true;
        }

        string prefix = optionName + "=";
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = arg[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        return true;
    }

    private sealed record CliSessionOptions(
        string? SectionId,
        string? ProfileName,
        string? ThinkingMode);
}
