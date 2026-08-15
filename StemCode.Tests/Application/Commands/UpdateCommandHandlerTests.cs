using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;
using System.Collections.Generic;

namespace StemCode.Tests.Application.Commands;

public sealed class UpdateCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReportCurrentVersion_When_NoUpdateIsAvailable()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.3",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: false);

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningStemCodeInstance>());
        Mock<IStatusMessageWriter> statusMessageWriter = new();
        UpdateCommandHandler sut = new(
            updateService.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            instanceService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be("StemCode is up to date. Current version: 1.2.3.");
        updateService.Verify(service => service.InstallAsync(It.IsAny<ApplicationUpdateInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        confirmationPrompt.VerifyNoOtherCalls();
        instanceService.Verify(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()), Times.Never);
        instanceService.Verify(service => service.TerminateAsync(It.IsAny<RunningStemCodeInstance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Should_InstallUpdate_When_NowArgumentIsUsed()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: true);
        ApplicationUpdateInstallResult installResult = new(
            IsSuccess: true,
            "StemCode update installed: 1.2.4. Restart StemCode to use the new version.");

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);
        updateService
            .Setup(service => service.InstallAsync(updateInfo, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(installResult);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningStemCodeInstance>());
        Mock<IStatusMessageWriter> statusMessageWriter = new();
        UpdateCommandHandler sut = new(
            updateService.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            instanceService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("now"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be(installResult.Message);
        updateService.VerifyAll();
        confirmationPrompt.VerifyNoOtherCalls();
    instanceService.Verify(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()), Times.Once);
    instanceService.Verify(service => service.TerminateAsync(It.IsAny<RunningStemCodeInstance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Should_SkipInstall_When_UserDeclinesPrompt()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: true);

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(It.IsAny<ConfirmationPromptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningStemCodeInstance>());
        Mock<IStatusMessageWriter> statusMessageWriter = new();
        UpdateCommandHandler sut = new(
            updateService.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            instanceService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Skipped StemCode 1.2.4.");
        updateService.Verify(service => service.InstallAsync(It.IsAny<ApplicationUpdateInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        confirmationPrompt.VerifyAll();
    instanceService.Verify(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()), Times.Never);
    instanceService.Verify(service => service.TerminateAsync(It.IsAny<RunningStemCodeInstance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Should_TerminateOtherInstances_When_UserConfirms()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: true);
        ApplicationUpdateInstallResult installResult = new(
            IsSuccess: true,
            "StemCode update installed: 1.2.4. Restart StemCode to use the new version.");

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);
        updateService
            .Setup(service => service.InstallAsync(updateInfo, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(installResult);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.Is<ConfirmationPromptRequest>(request => request.Title.StartsWith("A StemCode update")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.Is<ConfirmationPromptRequest>(request => request.Title.StartsWith("Other StemCode")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningStemCodeInstance> { new(4242, "stemcode.exe") });
        instanceService
            .Setup(service => service.TerminateAsync(It.IsAny<RunningStemCodeInstance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IStatusMessageWriter> statusMessageWriter = new();
        UpdateCommandHandler sut = new(
            updateService.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            instanceService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be(installResult.Message);
        instanceService.Verify(
            service => service.TerminateAsync(
                It.Is<RunningStemCodeInstance>(instance => instance.ProcessId == 4242),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_TerminateOtherInstancesAutomatically_When_NowArgumentUsed()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: true);
        ApplicationUpdateInstallResult installResult = new(
            IsSuccess: true,
            "StemCode update installed: 1.2.4. Restart StemCode to use the new version.");

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);
        updateService
            .Setup(service => service.InstallAsync(updateInfo, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(installResult);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningStemCodeInstance> { new(4242, "StemCode.CLI") });
        instanceService
            .Setup(service => service.TerminateAsync(It.IsAny<RunningStemCodeInstance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IStatusMessageWriter> statusMessageWriter = new();
        UpdateCommandHandler sut = new(
            updateService.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            instanceService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("now"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be(installResult.Message);
        // No confirmation prompt is shown in 'now' mode; termination happens automatically.
        confirmationPrompt.VerifyNoOtherCalls();
        instanceService.Verify(
            service => service.TerminateAsync(
                It.Is<RunningStemCodeInstance>(instance => instance.ProcessId == 4242),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_SkipTermination_When_UserDeclines()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.4",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: true);
        ApplicationUpdateInstallResult installResult = new(
            IsSuccess: true,
            "StemCode update installed: 1.2.4. Restart StemCode to use the new version.");

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);
        updateService
            .Setup(service => service.InstallAsync(updateInfo, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(installResult);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.Is<ConfirmationPromptRequest>(request => request.Title.StartsWith("A StemCode update")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.Is<ConfirmationPromptRequest>(request => request.Title.StartsWith("Other StemCode")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunningStemCodeInstance> { new(4242, "stemcode.exe") });

        Mock<IStatusMessageWriter> statusMessageWriter = new();
        UpdateCommandHandler sut = new(
            updateService.Object,
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            instanceService.Object);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Be(installResult.Message);
        instanceService.Verify(
            service => service.TerminateAsync(It.IsAny<RunningStemCodeInstance>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
