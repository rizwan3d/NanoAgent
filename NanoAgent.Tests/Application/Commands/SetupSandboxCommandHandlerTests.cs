using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class SetupSandboxCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReportReady_WhenSetupSucceeds()
    {
        Mock<IWindowsSandboxStartupService> startupService = new(MockBehavior.Strict);
        startupService
            .Setup(service => service.SetupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WindowsSandboxSetupResult(
                WindowsSandboxSetupState.Ready,
                "Windows sandbox setup is ready."));

        SetupSandboxCommandHandler sut = new(startupService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be("Windows sandbox setup is ready.");
        startupService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReportWarning_WhenSetupDoesNotComplete()
    {
        Mock<IWindowsSandboxStartupService> startupService = new(MockBehavior.Strict);
        startupService
            .Setup(service => service.SetupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WindowsSandboxSetupResult(
                WindowsSandboxSetupState.Canceled,
                "Windows sandbox setup was canceled. Restricted Windows shell commands may fail until setup is completed."));

        SetupSandboxCommandHandler sut = new(startupService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Warning);
        result.Message.Should().Be("Windows sandbox setup was canceled. Restricted Windows shell commands may fail until setup is completed.");
        startupService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectUnexpectedArguments()
    {
        Mock<IWindowsSandboxStartupService> startupService = new(MockBehavior.Strict);
        SetupSandboxCommandHandler sut = new(startupService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("now"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Error);
        result.Message.Should().Be("Usage: /setup-sandbox");
        startupService.VerifyNoOtherCalls();
    }

    private static ReplCommandContext CreateContext(string argumentText)
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null),
            "gpt-4.1",
            ["gpt-4.1"]);

        string[] arguments = string.IsNullOrWhiteSpace(argumentText)
            ? []
            : argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ReplCommandContext(
            "setup-sandbox",
            argumentText,
            arguments,
            string.IsNullOrWhiteSpace(argumentText) ? "/setup-sandbox" : $"/setup-sandbox {argumentText}",
            session);
    }
}
