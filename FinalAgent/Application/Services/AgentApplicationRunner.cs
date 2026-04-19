using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Logging;
using FinalAgent.Application.Models;
using FinalAgent.Domain.Models;
using FinalAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinalAgent.Application.Services;

internal sealed class AgentApplicationRunner : IApplicationRunner
{
    private readonly IFirstRunOnboardingService _onboardingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly IReplRuntime _replRuntime;
    private readonly ApplicationOptions _options;
    private readonly ILogger<AgentApplicationRunner> _logger;

    public AgentApplicationRunner(
        IFirstRunOnboardingService onboardingService,
        IModelDiscoveryService modelDiscoveryService,
        IReplRuntime replRuntime,
        IOptions<ApplicationOptions> options,
        ILogger<AgentApplicationRunner> logger)
    {
        _onboardingService = onboardingService;
        _modelDiscoveryService = modelDiscoveryService;
        _replRuntime = replRuntime;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ApplicationLogMessages.RunnerStarted(_logger, _options.ProductName);

        OnboardingResult result = await _onboardingService.EnsureOnboardedAsync(cancellationToken);
        ModelDiscoveryResult modelResult = await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken);

        ApplicationLogMessages.ModelDiscoveryCompleted(
            _logger,
            modelResult.SelectedModelId,
            modelResult.SelectionSource.ToString());

        await _replRuntime.RunAsync(
            new ReplSessionContext(
                result.Profile,
                modelResult.SelectedModelId),
            cancellationToken);

        ApplicationLogMessages.RunnerCompleted(
            _logger,
            result.Profile.ProviderKind.ToDisplayName(),
            result.WasOnboardedDuringCurrentRun);
    }
}
