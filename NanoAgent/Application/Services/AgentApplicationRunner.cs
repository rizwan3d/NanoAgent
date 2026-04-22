using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Application.Services;

internal sealed class AgentApplicationRunner : IApplicationRunner
{
    private readonly IFirstRunOnboardingService _onboardingService;
    private readonly IModelDiscoveryService _modelDiscoveryService;
    private readonly ISessionAppService _sessionAppService;
    private readonly IReplRuntime _replRuntime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentApplicationRunner> _logger;

    public AgentApplicationRunner(
        IFirstRunOnboardingService onboardingService,
        IModelDiscoveryService modelDiscoveryService,
        ISessionAppService sessionAppService,
        IReplRuntime replRuntime,
        IConfiguration configuration,
        ILogger<AgentApplicationRunner> logger)
    {
        _onboardingService = onboardingService;
        _modelDiscoveryService = modelDiscoveryService;
        _sessionAppService = sessionAppService;
        _replRuntime = replRuntime;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ApplicationLogMessages.RunnerStarted(_logger, ApplicationIdentity.ProductName);

        OnboardingResult result = await _onboardingService.EnsureOnboardedAsync(cancellationToken);
        ReplSessionContext session;
        string? requestedSectionId = NormalizeRequestedSectionId(_configuration["section"]);
        string? requestedProfileName = NormalizeRequestedProfileName(_configuration["profile"]);

        if (requestedSectionId is null)
        {
            ModelDiscoveryResult modelResult = await _modelDiscoveryService.DiscoverAndSelectAsync(cancellationToken);

            ApplicationLogMessages.ModelDiscoveryCompleted(
                _logger,
                modelResult.SelectedModelId,
                modelResult.SelectionSource.ToString());

            session = await _sessionAppService.CreateAsync(
                new CreateSessionRequest(
                    result.Profile,
                    modelResult.SelectedModelId,
                    modelResult.AvailableModels.Select(static model => model.Id).ToArray(),
                    requestedProfileName),
                cancellationToken);
        }
        else
        {
            session = await _sessionAppService.ResumeAsync(
                new ResumeSessionRequest(
                    requestedSectionId,
                    requestedProfileName),
                cancellationToken);
        }

        await _replRuntime.RunAsync(
            session,
            cancellationToken);

        ApplicationLogMessages.RunnerCompleted(
            _logger,
            session.ProviderProfile.ProviderKind.ToDisplayName(),
            result.WasOnboardedDuringCurrentRun);
    }

    private static string? NormalizeRequestedSectionId(string? sectionId)
    {
        return string.IsNullOrWhiteSpace(sectionId)
            ? null
            : sectionId.Trim();
    }

    private static string? NormalizeRequestedProfileName(string? profileName)
    {
        return string.IsNullOrWhiteSpace(profileName)
            ? null
            : profileName.Trim();
    }
}
