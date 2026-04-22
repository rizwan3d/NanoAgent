using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;
using FluentAssertions;
using Moq;

namespace NanoAgent.Tests.Application.Services;

public sealed class SessionAppServiceTests
{
    [Fact]
    public async Task CreateNewAsync_Should_ResolveProfileAndDelegateToSectionService()
    {
        BuiltInAgentProfileResolver profileResolver = new();
        AgentProviderProfile providerProfile = new(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1");
        ReplSessionContext createdSession = new(
            "NanoAgent",
            providerProfile,
            "gpt-5-mini",
            ["gpt-5-mini"],
            agentProfile: profileResolver.Resolve("plan"));

        Mock<IReplSectionService> sectionService = new(MockBehavior.Strict);
        sectionService
            .Setup(service => service.CreateNewAsync(
                "NanoAgent",
                providerProfile,
                "gpt-5-mini",
                It.Is<IReadOnlyList<string>>(models => models.SequenceEqual(new[] { "gpt-5-mini" })),
                It.Is<IAgentProfile>(profile => profile.Name == "plan"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdSession);

        SessionAppService sut = new(
            profileResolver,
            Mock.Of<IConversationSectionStore>(),
            sectionService.Object);

        ReplSessionContext result = await sut.CreateNewAsync(
            "NanoAgent",
            providerProfile,
            "gpt-5-mini",
            ["gpt-5-mini"],
            "plan",
            CancellationToken.None);

        result.AgentProfile.Name.Should().Be("plan");
        sectionService.VerifyAll();
    }

    [Fact]
    public async Task ResumeAsync_Should_UseSavedProfile_When_ProfileNameIsMissing()
    {
        string sectionId = Guid.NewGuid().ToString("D");
        BuiltInAgentProfileResolver profileResolver = new();
        ReplSessionContext resumedSession = new(
            "NanoAgent",
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"],
            sectionId,
            "Saved section",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            isResumedSection: true,
            agentProfile: profileResolver.Resolve("review"));

        Mock<IReplSectionService> sectionService = new(MockBehavior.Strict);
        sectionService
            .Setup(service => service.ResumeAsync(
                "NanoAgent",
                sectionId,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumedSession);

        SessionAppService sut = new(
            profileResolver,
            Mock.Of<IConversationSectionStore>(),
            sectionService.Object);

        ReplSessionContext result = await sut.ResumeAsync(
            "NanoAgent",
            sectionId,
            profileName: null,
            CancellationToken.None);

        result.AgentProfile.Name.Should().Be("review");
        sectionService.VerifyAll();
    }

    [Fact]
    public async Task ListAsync_Should_ReturnSnapshotsFromStore()
    {
        ConversationSectionSnapshot snapshot = new(
            Guid.NewGuid().ToString("D"),
            "Existing session",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-5-mini",
            ["gpt-5-mini"],
            [],
            0,
            agentProfileName: "build");

        Mock<IConversationSectionStore> sectionStore = new(MockBehavior.Strict);
        sectionStore
            .Setup(store => store.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([snapshot]);

        SessionAppService sut = new(
            new BuiltInAgentProfileResolver(),
            sectionStore.Object,
            Mock.Of<IReplSectionService>());

        IReadOnlyList<ConversationSectionSnapshot> result = await sut.ListAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeSameAs(snapshot);
        sectionStore.VerifyAll();
    }
}
