using FluentAssertions;
using NanoAgent.Domain.Models;
using NanoAgent.Domain.Services;

namespace NanoAgent.Tests.Domain.Services;

public sealed class AgentProviderProfileFactoryTests
{
    private readonly AgentProviderProfileFactory _sut = new();

    [Fact]
    public void CreateOpenAi_Should_ReturnOpenAiProfile_When_Called()
    {
        AgentProviderProfile profile = _sut.CreateOpenAi();

        profile.Should().Be(new AgentProviderProfile(ProviderKind.OpenAi, null));
    }

    [Fact]
    public void CreateOpenAiChatGptAccount_Should_ReturnOpenAiChatGptAccountProfile_When_Called()
    {
        AgentProviderProfile profile = _sut.CreateOpenAiChatGptAccount();

        profile.Should().Be(new AgentProviderProfile(ProviderKind.OpenAiChatGptAccount, null));
    }

    [Fact]
    public void CreateOpenRouter_Should_ReturnOpenRouterProfile_When_Called()
    {
        AgentProviderProfile profile = _sut.CreateOpenRouter();

        profile.Should().Be(new AgentProviderProfile(ProviderKind.OpenRouter, null));
    }

    [Fact]
    public void CreateGoogleAiStudio_Should_ReturnGoogleAiStudioProfile_When_Called()
    {
        AgentProviderProfile profile = _sut.CreateGoogleAiStudio();

        profile.Should().Be(new AgentProviderProfile(ProviderKind.GoogleAiStudio, null));
    }

    [Fact]
    public void CreateAnthropic_Should_ReturnAnthropicProfile_When_Called()
    {
        AgentProviderProfile profile = _sut.CreateAnthropic();

        profile.Should().Be(new AgentProviderProfile(ProviderKind.Anthropic, null));
    }

    [Fact]
    public void CreateCompatible_Should_NormalizeTrailingSlash_When_BaseUrlIsProvided()
    {
        AgentProviderProfile profile = _sut.CreateCompatible(" https://provider.example.com/v1/ ");

        profile.Should().Be(new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"));
    }

    [Fact]
    public void CreateCompatible_Should_AppendV1_When_RootBaseUrlIsProvided()
    {
        AgentProviderProfile profile = _sut.CreateCompatible(" http://127.0.0.1:1234 ");

        profile.Should().Be(new AgentProviderProfile(ProviderKind.OpenAiCompatible, "http://127.0.0.1:1234/v1"));
    }

    [Fact]
    public void CreateCompatible_Should_ThrowArgumentException_When_BaseUrlIsWhitespace()
    {
        Action act = () => _sut.CreateCompatible("  ");

        act.Should().Throw<ArgumentException>();
    }
}
