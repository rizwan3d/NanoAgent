using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.WindowsSandbox;

namespace NanoAgent.Tests.Infrastructure.WindowsSandbox;

public sealed class WindowsSandboxStartupServiceTests
{
    [Fact]
    public async Task EnsureReadyAsync_Should_Skip_WhenStartupPromptsAreDisabled()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupResult = true
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: false);

        await sut.EnsureReadyAsync(CancellationToken.None);

        bootstrapper.EnsureSetupCallCount.Should().Be(0);
        confirmationPrompt.VerifyNoOtherCalls();
        statusMessageWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnsureReadyAsync_Should_PromptAndSetup_WhenSetupIsRequired()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.Is<ConfirmationPromptRequest>(request =>
                    request.Title == "Windows sandbox setup required. Set it up now?" &&
                    request.DefaultValue),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Setting up Windows sandbox. Windows may ask for administrator approval.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Windows sandbox setup is ready.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupResult = true
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: true);

        await sut.EnsureReadyAsync(CancellationToken.None);

        bootstrapper.EnsureSetupCallCount.Should().Be(1);
        confirmationPrompt.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureReadyAsync_Should_ReportSkip_WhenUserDeclinesSetup()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.IsAny<ConfirmationPromptRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Skipped Windows sandbox setup. Restricted Windows shell commands may fail until setup is completed.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupResult = true
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: true);

        await sut.EnsureReadyAsync(CancellationToken.None);

        bootstrapper.EnsureSetupCallCount.Should().Be(0);
        confirmationPrompt.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureReadyAsync_Should_ReportCancellation_WhenSetupIsCanceled()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);
        confirmationPrompt
            .Setup(prompt => prompt.PromptAsync(
                It.IsAny<ConfirmationPromptRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Setting up Windows sandbox. Windows may ask for administrator approval.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Windows sandbox setup was canceled. Restricted Windows shell commands may fail until setup is completed.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupResult = true,
            ExceptionToThrow = new OperationCanceledException("Canceled.")
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: true);

        await sut.EnsureReadyAsync(CancellationToken.None);

        bootstrapper.EnsureSetupCallCount.Should().Be(1);
        confirmationPrompt.VerifyAll();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task EnsureReadyAsync_Should_ReportCheckFailure_AndContinue()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowErrorAsync(
                "Windows sandbox setup check failed: probe failed",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupException = new InvalidOperationException("probe failed")
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: true);

        await sut.EnsureReadyAsync(CancellationToken.None);

        bootstrapper.EnsureSetupCallCount.Should().Be(0);
        confirmationPrompt.VerifyNoOtherCalls();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task SetupAsync_Should_SetupImmediately_WhenSetupIsRequired()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Setting up Windows sandbox. Windows may ask for administrator approval.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        statusMessageWriter
            .Setup(writer => writer.ShowSuccessAsync(
                "Windows sandbox setup is ready.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupResult = true
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: false);

        WindowsSandboxSetupResult result = await sut.SetupAsync(CancellationToken.None);

        result.State.Should().Be(WindowsSandboxSetupState.Ready);
        bootstrapper.EnsureSetupCallCount.Should().Be(1);
        confirmationPrompt.VerifyNoOtherCalls();
        statusMessageWriter.VerifyAll();
    }

    [Fact]
    public async Task SetupAsync_Should_ReportAlreadyReady_WhenSetupIsNotRequired()
    {
        Mock<IConfirmationPrompt> confirmationPrompt = new(MockBehavior.Strict);

        Mock<IStatusMessageWriter> statusMessageWriter = new(MockBehavior.Strict);
        statusMessageWriter
            .Setup(writer => writer.ShowInfoAsync(
                "Windows sandbox setup is already ready.",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        FakeWindowsSandboxSetupBootstrapper bootstrapper = new()
        {
            RequiresSetupResult = false
        };
        WindowsSandboxStartupService sut = CreateSut(
            confirmationPrompt.Object,
            statusMessageWriter.Object,
            bootstrapper,
            enableStartupPrompts: false);

        WindowsSandboxSetupResult result = await sut.SetupAsync(CancellationToken.None);

        result.State.Should().Be(WindowsSandboxSetupState.AlreadyReady);
        bootstrapper.EnsureSetupCallCount.Should().Be(0);
        confirmationPrompt.VerifyNoOtherCalls();
        statusMessageWriter.VerifyAll();
    }

    private static WindowsSandboxStartupService CreateSut(
        IConfirmationPrompt confirmationPrompt,
        IStatusMessageWriter statusMessageWriter,
        IWindowsSandboxSetupBootstrapper bootstrapper,
        bool enableStartupPrompts)
    {
        return new WindowsSandboxStartupService(
            new BackendRuntimeOptions(enableStartupPrompts: enableStartupPrompts),
            confirmationPrompt,
            statusMessageWriter,
            bootstrapper);
    }

    private sealed class FakeWindowsSandboxSetupBootstrapper : IWindowsSandboxSetupBootstrapper
    {
        public bool RequiresSetupResult { get; init; }

        public Exception? RequiresSetupException { get; init; }

        public Exception? ExceptionToThrow { get; init; }

        public int EnsureSetupCallCount { get; private set; }

        public bool RequiresSetup()
        {
            if (RequiresSetupException is not null)
            {
                throw RequiresSetupException;
            }

            return RequiresSetupResult;
        }

        public void EnsureSetup()
        {
            EnsureSetupCallCount++;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }
}
