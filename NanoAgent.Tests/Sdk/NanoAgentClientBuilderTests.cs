using FluentAssertions;
using NanoAgent.Domain.Models;
using NanoAgent.Sdk;

namespace NanoAgent.Tests.Sdk;

public sealed class NanoAgentClientBuilderTests
{
    [Fact]
    public void Build_Should_Throw_When_NoProviderConfigured()
    {
        NanoAgentClientBuilder builder = NanoAgentClient.CreateBuilder();

        Action act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*provider must be configured*");
    }

    [Fact]
    public void Build_Should_Throw_When_HostedProviderHasNoApiKey()
    {
        NanoAgentClientBuilder builder = NanoAgentClient.CreateBuilder()
            .UseOpenAi(string.Empty);

        Action act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*API key is required*");
    }

    [Fact]
    public void Build_Should_Succeed_For_KeylessLocalProvider()
    {
        NanoAgentClientBuilder builder = NanoAgentClient.CreateBuilder()
            .UseOllama();

        NanoAgentClient client = builder.Build();

        client.Should().NotBeNull();
    }

    [Fact]
    public void Build_Should_Succeed_For_HostedProviderWithApiKey()
    {
        NanoAgentClientBuilder builder = NanoAgentClient.CreateBuilder()
            .UseAnthropic("sk-test", "claude-opus-4-8")
            .WithWorkspace(Directory.GetCurrentDirectory())
            .AutoApproveTools();

        NanoAgentClient client = builder.Build();

        client.Should().NotBeNull();
    }

    [Fact]
    public void WithThinkingMode_Should_RejectUnsupportedValue()
    {
        NanoAgentClientBuilder builder = NanoAgentClient.CreateBuilder();

        Action act = () => builder.WithThinkingMode("turbo");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UseProvider_Should_AcceptExplicitProfile()
    {
        AgentProviderProfile profile = new(ProviderKind.OpenAiCompatible, "https://api.example.com/v1");

        NanoAgentClient client = NanoAgentClient.CreateBuilder()
            .UseProvider(profile, "sk-test")
            .Build();

        client.Should().NotBeNull();
    }
}
