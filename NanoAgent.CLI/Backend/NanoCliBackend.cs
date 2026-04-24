using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.DependencyInjection;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.DependencyInjection;
using NanoAgent.Presentation.Abstractions;
using NanoAgent.Presentation.DependencyInjection;

namespace NanoAgent.CLI;

public sealed class NanoCliBackend : IAsyncDisposable
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
        UiBridge uiBridge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uiBridge);

        if (_session is not null)
        {
            return new BackendSessionInfo(
                _session.ProviderName,
                _session.ActiveModelId);
        }

        _host = CreateHost(uiBridge, _args);
        _onboardingService = _host.Services.GetRequiredService<IFirstRunOnboardingService>();
        _modelDiscoveryService = _host.Services.GetRequiredService<IModelDiscoveryService>();
        _sessionAppService = _host.Services.GetRequiredService<ISessionAppService>();
        _agentTurnService = _host.Services.GetRequiredService<IAgentTurnService>();
        _commandParser = _host.Services.GetRequiredService<IReplCommandParser>();
        _commandDispatcher = _host.Services.GetRequiredService<IReplCommandDispatcher>();

        OnboardingResult onboardingResult = await _onboardingService.EnsureOnboardedAsync(cancellationToken);
        ModelDiscoveryResult modelResult = await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken);

        _session = await _sessionAppService.CreateAsync(
            new CreateSessionRequest(
                onboardingResult.Profile,
                modelResult.SelectedModelId,
                modelResult.AvailableModels.Select(static model => model.Id).ToArray(),
                ProfileName: null,
                ReasoningEffort: onboardingResult.ReasoningEffort),
            cancellationToken);

        return new BackendSessionInfo(
            _session.ProviderName,
            _session.ActiveModelId);
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
            new BackendSessionInfo(
                _session.ProviderName,
                _session.ActiveModelId));
    }

    public async Task<ConversationTurnResult> RunTurnAsync(
        string input,
        UiBridge uiBridge,
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

    private static IHost CreateHost(UiBridge uiBridge, string[] args)
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
            .AddPresentation()
            .AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton<ISelectionPrompt, UiSelectionPrompt>();
        builder.Services.AddSingleton<ITextPrompt, UiTextPrompt>();
        builder.Services.AddSingleton<ISecretPrompt, UiSecretPrompt>();
        builder.Services.AddSingleton<IConfirmationPrompt, UiConfirmationPrompt>();
        builder.Services.AddSingleton<IStatusMessageWriter, UiStatusMessageWriter>();

        return builder.Build();
    }
}
