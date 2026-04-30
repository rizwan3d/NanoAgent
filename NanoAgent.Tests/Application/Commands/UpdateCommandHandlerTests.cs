using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class UpdateCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReportCurrentVersion_When_NoUpdateIsAvailable()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.3",
            new Uri("https://github.com/rizwan3d/NanoAgent/releases/latest"),
            IsUpdateAvailable: false);

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        UpdateCommandHandler sut = new(updateService.Object, confirmationPrompt.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be("NanoAgent is up to date. Current version: 1.2.3.");
        updateService.Verify(service => service.InstallAsync(It.IsAny<ApplicationUpdateInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        confirmationPrompt.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Should_InstallUpdate_When_NowArgumentIsUsed()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/NanoAgent/releases/latest"),
            IsUpdateAvailable: true);
        ApplicationUpdateInstallResult installResult = new(
            IsSuccess: true,
            "NanoAgent update installed: 1.2.4. Restart NanoAgent to use the new version.");

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);
        updateService
            .Setup(service => service.InstallAsync(updateInfo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(installResult);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        UpdateCommandHandler sut = new(updateService.Object, confirmationPrompt.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("now"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be(installResult.Message);
        updateService.VerifyAll();
        confirmationPrompt.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_Should_SkipInstall_When_UserDeclinesPrompt()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/NanoAgent/releases/latest"),
            IsUpdateAvailable: true);

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<ConfirmationPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        UpdateCommandHandler sut = new(updateService.Object, confirmationPrompt.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Skipped NanoAgent 1.2.4.");
        updateService.Verify(service => service.InstallAsync(It.IsAny<ApplicationUpdateInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        confirmationPrompt.VerifyAll();
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
            "update",
            argumentText,
            arguments,
            string.IsNullOrWhiteSpace(argumentText) ? "/update" : $"/update {argumentText}",
            session);
    }
}
