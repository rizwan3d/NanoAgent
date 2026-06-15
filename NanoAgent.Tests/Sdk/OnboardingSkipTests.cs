using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;
using NanoAgent.Sdk.Internal;

namespace NanoAgent.Tests.Sdk;

/// <summary>
/// Proves the SDK's core promise: when a provider and API key are supplied
/// programmatically (via the in-memory stores), the first-run onboarding flow
/// detects a complete configuration and returns without invoking any interactive
/// prompt — so an embedding host never blocks waiting on a console.
/// </summary>
public sealed class OnboardingSkipTests
{
    [Fact]
    public async Task EnsureOnboardedAsync_Should_SkipPrompts_When_ConfigurationIsSeeded()
    {
        AgentProviderProfile profile = new AgentProviderProfileFactory().CreateAnthropic();
        InMemoryAgentConfigurationStore configurationStore = new(
            new AgentConfiguration(profile, PreferredModelId: "claude-opus-4-8", ActiveProviderName: "Anthropic"));
        InMemoryApiKeySecretStore secretStore = new("sk-test");

        // Strict mocks fail the test if any interactive prompt is invoked.
        Mock<ISelectionPrompt> selectionPrompt = new(MockBehavior.Strict);
        Mock<ITextPrompt> textPrompt = new(MockBehavior.Strict);
        Mock<ISecretPrompt> secretPrompt = new(MockBehavior.Strict);
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new();
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FirstRunOnboardingService sut = new(
            selectionPrompt.Object,
            textPrompt.Object,
            secretPrompt.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            new Mock<IOnboardingInputValidator>().Object,
            configurationStore,
            secretStore,
            new AgentProviderProfileFactory(),
            new Mock<ILogger<FirstRunOnboardingService>>().Object);

        OnboardingResult result = await sut.EnsureOnboardedAsync(CancellationToken.None);

        result.WasOnboardedDuringCurrentRun.Should().BeFalse();
        result.Profile.ProviderKind.Should().Be(ProviderKind.Anthropic);
        result.ActiveProviderName.Should().Be("Anthropic");
    }
}
