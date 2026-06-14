using FluentAssertions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

[Collection(global::NanoAgent.Tests.TestCollections.SecretRedactorState)]
public sealed class RedactCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReportCurrentStatus_When_NoArgumentsAreProvided()
    {
        bool originalValue = SecretRedactor.IsEnabled;
        SecretRedactor.IsEnabled = true;
        RedactCommandHandler sut = new();

        try
        {
            ReplCommandResult result = await sut.ExecuteAsync(
                CreateContext(string.Empty),
                CancellationToken.None);

            result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
            result.Message.Should().Be("Secret redaction: on. Use /redact on or /redact off.");
        }
        finally
        {
            SecretRedactor.IsEnabled = originalValue;
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_ToggleSecretRedactionOff()
    {
        bool originalValue = SecretRedactor.IsEnabled;
        SecretRedactor.IsEnabled = true;
        RedactCommandHandler sut = new();

        try
        {
            ReplCommandResult result = await sut.ExecuteAsync(
                CreateContext("off"),
                CancellationToken.None);

            result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
            result.Message.Should().Be("Secret redaction turned off.");
            SecretRedactor.IsEnabled.Should().BeFalse();
        }
        finally
        {
            SecretRedactor.IsEnabled = originalValue;
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectUnsupportedValues()
    {
        bool originalValue = SecretRedactor.IsEnabled;
        RedactCommandHandler sut = new();

        try
        {
            ReplCommandResult result = await sut.ExecuteAsync(
                CreateContext("maybe"),
                CancellationToken.None);

            result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
            result.Message.Should().Be("Unsupported value 'maybe'. Use /redact on or /redact off.");
            SecretRedactor.IsEnabled.Should().Be(originalValue);
        }
        finally
        {
            SecretRedactor.IsEnabled = originalValue;
        }
    }

    private static ReplCommandContext CreateContext(string argumentText)
    {
        string normalizedArgumentText = argumentText.Trim();
        return new ReplCommandContext(
            "redact",
            normalizedArgumentText,
            string.IsNullOrWhiteSpace(normalizedArgumentText) ? [] : [normalizedArgumentText],
            string.IsNullOrWhiteSpace(normalizedArgumentText) ? "/redact" : $"/redact {normalizedArgumentText}",
            new ReplSessionContext(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                "model-a",
                ["model-a"]));
    }
}
