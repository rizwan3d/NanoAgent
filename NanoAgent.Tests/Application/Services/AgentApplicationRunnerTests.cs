using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace NanoAgent.Tests.Application.Services;

public sealed class AgentApplicationRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_CreateNewSection_When_SectionArgumentIsMissing()
    {
        OnboardingResult onboardingResult = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            false);
        ModelDiscoveryResult modelDiscoveryResult = new(
            [new AvailableModel("gpt-5-mini")],
            "gpt-5-mini",
            ModelSelectionSource.FirstReturnedModel,
            ConfiguredDefaultModelStatus.NotConfigured,
            null,
            false);
        ReplSessionContext createdSession = new(
            "NanoAgent",
            onboardingResult.Profile,
            "gpt-5-mini",
            ["gpt-5-mini"]);

        Mock<IFirstRunOnboardingService> onboardingService = new(MockBehavior.Strict);
        onboardingService
            .Setup(service => service.EnsureOnboardedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(onboardingResult);

        Mock<IModelDiscoveryService> modelDiscoveryService = new(MockBehavior.Strict);
        modelDiscoveryService
            .Setup(service => service.DiscoverAndSelectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelDiscoveryResult);

        Mock<ISessionAppService> sessionAppService = new(MockBehavior.Strict);
        sessionAppService
            .Setup(service => service.CreateAsync(
                It.Is<CreateSessionRequest>(request =>
                    request.ProviderProfile == onboardingResult.Profile &&
                    request.ActiveModelId == "gpt-5-mini" &&
                    request.AvailableModelIds.SequenceEqual(new[] { "gpt-5-mini" }) &&
                    request.ProfileName is null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdSession);

        Mock<IReplRuntime> replRuntime = new(MockBehavior.Strict);
        replRuntime
            .Setup(runtime => runtime.RunAsync(createdSession, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AgentApplicationRunner sut = new(
            onboardingService.Object,
            modelDiscoveryService.Object,
            sessionAppService.Object,
            replRuntime.Object,
            BuildConfiguration(),
            NullLogger<AgentApplicationRunner>.Instance);

        await sut.RunAsync(CancellationToken.None);

        sessionAppService.VerifyAll();
        replRuntime.VerifyAll();
    }

    [Fact]
    public async Task RunAsync_Should_CreateNewSectionWithRequestedProfile_When_ProfileArgumentIsProvided()
    {
        OnboardingResult onboardingResult = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            false);
        ModelDiscoveryResult modelDiscoveryResult = new(
            [new AvailableModel("gpt-5-mini")],
            "gpt-5-mini",
            ModelSelectionSource.FirstReturnedModel,
            ConfiguredDefaultModelStatus.NotConfigured,
            null,
            false);
        ReplSessionContext createdSession = new(
            "NanoAgent",
            onboardingResult.Profile,
            "gpt-5-mini",
            ["gpt-5-mini"]);

        Mock<IFirstRunOnboardingService> onboardingService = new(MockBehavior.Strict);
        onboardingService
            .Setup(service => service.EnsureOnboardedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(onboardingResult);

        Mock<IModelDiscoveryService> modelDiscoveryService = new(MockBehavior.Strict);
        modelDiscoveryService
            .Setup(service => service.DiscoverAndSelectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelDiscoveryResult);

        Mock<ISessionAppService> sessionAppService = new(MockBehavior.Strict);
        sessionAppService
            .Setup(service => service.CreateAsync(
                It.Is<CreateSessionRequest>(request =>
                    request.ProviderProfile == onboardingResult.Profile &&
                    request.ActiveModelId == "gpt-5-mini" &&
                    request.AvailableModelIds.SequenceEqual(new[] { "gpt-5-mini" }) &&
                    request.ProfileName == "review"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdSession);

        Mock<IReplRuntime> replRuntime = new(MockBehavior.Strict);
        replRuntime
            .Setup(runtime => runtime.RunAsync(createdSession, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AgentApplicationRunner sut = new(
            onboardingService.Object,
            modelDiscoveryService.Object,
            sessionAppService.Object,
            replRuntime.Object,
            BuildConfiguration(profileName: "review"),
            NullLogger<AgentApplicationRunner>.Instance);

        await sut.RunAsync(CancellationToken.None);

        sessionAppService.VerifyAll();
        replRuntime.VerifyAll();
    }

    [Fact]
    public async Task RunAsync_Should_ResumeRequestedSection_When_SectionArgumentIsProvided()
    {
        string sectionId = Guid.NewGuid().ToString("D");
        OnboardingResult onboardingResult = new(
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            false);
        ReplSessionContext resumedSession = new(
            "NanoAgent",
            onboardingResult.Profile,
            "gpt-5-mini",
            ["gpt-5-mini"],
            sectionId,
            "Todo App Session",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            isResumedSection: true);

        Mock<IFirstRunOnboardingService> onboardingService = new(MockBehavior.Strict);
        onboardingService
            .Setup(service => service.EnsureOnboardedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(onboardingResult);

        Mock<IModelDiscoveryService> modelDiscoveryService = new(MockBehavior.Strict);
        Mock<ISessionAppService> sessionAppService = new(MockBehavior.Strict);
        sessionAppService
            .Setup(service => service.ResumeAsync(
                It.Is<ResumeSessionRequest>(request =>
                    request.SessionId == sectionId &&
                    request.ProfileName is null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumedSession);

        Mock<IReplRuntime> replRuntime = new(MockBehavior.Strict);
        replRuntime
            .Setup(runtime => runtime.RunAsync(resumedSession, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AgentApplicationRunner sut = new(
            onboardingService.Object,
            modelDiscoveryService.Object,
            sessionAppService.Object,
            replRuntime.Object,
            BuildConfiguration(sectionId),
            NullLogger<AgentApplicationRunner>.Instance);

        await sut.RunAsync(CancellationToken.None);

        modelDiscoveryService.Verify(
            service => service.DiscoverAndSelectAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        sessionAppService.VerifyAll();
        replRuntime.VerifyAll();
    }

    private static IConfiguration BuildConfiguration(
        string? sectionId = null,
        string? profileName = null)
    {
        Dictionary<string, string?> values = [];
        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            values["section"] = sectionId;
        }

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            values["profile"] = profileName;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
