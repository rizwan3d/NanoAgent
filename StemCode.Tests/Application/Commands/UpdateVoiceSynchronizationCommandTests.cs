using FluentAssertions;
using Moq;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Commands;

public sealed class UpdateVoiceSynchronizationCommandTests
{
    [Fact]
    public async Task ExecuteAsync_UpdateNow_Should_SynchronizeVoice_WhenCliIsAlreadyCurrent()
    {
        ApplicationUpdateInfo updateInfo = new(
            "1.2.3",
            "1.2.3",
            new Uri("https://github.com/rizwan3d/StemCode/releases/latest"),
            IsUpdateAvailable: false);
        ApplicationUpdateInstallResult installResult = new(
            IsSuccess: true,
            "StemCode and Voice runtime synchronization installed: 1.2.3.");

        Mock<IApplicationUpdateService> updateService = new(MockBehavior.Strict);
        updateService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateInfo);
        updateService
            .Setup(service => service.InstallAsync(
                updateInfo,
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(installResult);

        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStemCodeInstanceService> instanceService = new(MockBehavior.Strict);
        instanceService
            .Setup(service => service.GetOtherRunningInstancesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RunningStemCodeInstance>());
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
        instanceService.VerifyAll();
        statusMessageWriter.Verify(
            writer => writer.ShowInfoAsync(
                It.Is<string>(message => message.Contains("Synchronizing") && message.Contains("Voice runtime")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ReplCommandContext CreateContext(string argumentText)
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null),
            "gpt-4.1",
            ["gpt-4.1"]);

        return new ReplCommandContext(
            "update",
            argumentText,
            [argumentText],
            $"/update {argumentText}",
            session);
    }
}
