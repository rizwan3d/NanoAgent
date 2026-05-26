using FluentAssertions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Domain.Models;

public sealed class AgentProviderProfileTests
{
    [Fact]
    public void Should_Store_ProviderKind_And_BaseUrl()
    {
        var profile = new AgentProviderProfile(ProviderKind.OpenAi, "https://api.openai.com/v1");

        profile.ProviderKind.Should().Be(ProviderKind.OpenAi);
        profile.BaseUrl.Should().Be("https://api.openai.com/v1");
    }

    [Fact]
    public void Should_Allow_Null_BaseUrl()
    {
        var profile = new AgentProviderProfile(ProviderKind.Ollama, null);

        profile.ProviderKind.Should().Be(ProviderKind.Ollama);
        profile.BaseUrl.Should().BeNull();
    }

    [Fact]
    public void EqualProfiles_Should_Be_Equal()
    {
        var profile1 = new AgentProviderProfile(ProviderKind.OpenAi, "https://api.openai.com/v1");
        var profile2 = new AgentProviderProfile(ProviderKind.OpenAi, "https://api.openai.com/v1");

        profile1.Should().Be(profile2);
        (profile1 == profile2).Should().BeTrue();
    }

    [Fact]
    public void DifferentProfiles_Should_Not_Be_Equal()
    {
        var profile1 = new AgentProviderProfile(ProviderKind.OpenAi, null);
        var profile2 = new AgentProviderProfile(ProviderKind.Anthropic, null);

        profile1.Should().NotBe(profile2);
    }

    [Fact]
    public void ResolveBaseUrl_Should_UseLmStudioDefault_When_BaseUrlIsNull()
    {
        var profile = new AgentProviderProfile(ProviderKind.LmStudio, null);

        profile.ResolveBaseUrl().Should().Be("http://127.0.0.1:1234/v1");
    }

    [Fact]
    public void ResolveBaseUrl_Should_UseLmStudioCustomBaseUrl_When_BaseUrlIsProvided()
    {
        var profile = new AgentProviderProfile(ProviderKind.LmStudio, "http://127.0.0.1:4321");

        profile.ResolveBaseUrl().Should().Be("http://127.0.0.1:4321/v1");
    }
}
