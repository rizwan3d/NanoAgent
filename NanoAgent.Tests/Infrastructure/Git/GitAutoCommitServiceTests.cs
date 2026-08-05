using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Git;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Tests.Infrastructure.Secrets.TestDoubles;

namespace NanoAgent.Tests.Infrastructure.Git;

public sealed class GitAutoCommitServiceTests
{
    [Fact]
    public async Task TryAutoCommitAsync_Should_AddNanoAgentCoAuthorTrailerToAutoCommit()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.EnqueueResult(new ProcessExecutionResult(0, "true", string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, workspacePath, string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, "head", string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(1, string.Empty, string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));

            GitAutoCommitService sut = new(
                CreateSecretStoreMock().Object,
                new Mock<IConversationProviderClient>(MockBehavior.Strict).Object,
                new Mock<IConversationResponseMapper>(MockBehavior.Strict).Object,
                CreateConversationConfigurationAccessorMock().Object,
                processRunner);

            ReplSessionContext session = new(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                "model-a",
                ["model-a"],
                workspacePath: workspacePath);

            await sut.TryAutoCommitAsync(
                session,
                [new SessionEditContext(DateTimeOffset.UtcNow, "edit", ["src/test.txt"], 1, 0)],
                CancellationToken.None);

            ProcessExecutionRequest commitRequest = processRunner.Requests.Should().ContainSingle(
                request => request.FileName == "git" &&
                           request.Arguments.Count >= 1 &&
                           request.Arguments[0] == "commit").Subject;

            commitRequest.Arguments.Should().Equal(
                "commit",
                "-m",
                "chore: apply NanoAgent changes",
                "-m",
                "Co-authored-by: NanoAgentAi <313132566+NanoAgentAi@users.noreply.github.com>");
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryAutoCommitAsync_Should_Refuse_When_UnrelatedChangesAreAlreadyStaged()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            processRunner.EnqueueResult(new ProcessExecutionResult(0, "true", string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, workspacePath, string.Empty));
            processRunner.EnqueueResult(new ProcessExecutionResult(0, "docs/already-staged.md", string.Empty));

            GitAutoCommitService sut = new(
                CreateSecretStoreMock().Object,
                new Mock<IConversationProviderClient>(MockBehavior.Strict).Object,
                new Mock<IConversationResponseMapper>(MockBehavior.Strict).Object,
                CreateConversationConfigurationAccessorMock().Object,
                processRunner);

            ReplSessionContext session = new(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                "model-a",
                ["model-a"],
                workspacePath: workspacePath);

            await sut.TryAutoCommitAsync(
                session,
                [new SessionEditContext(DateTimeOffset.UtcNow, "edit", ["src/test.txt"], 1, 0)],
                CancellationToken.None);

            processRunner.Requests.Should().NotContain(
                request => request.FileName == "git" &&
                           request.Arguments.Count >= 1 &&
                           request.Arguments[0] == "commit");
            processRunner.Requests.Should().NotContain(
                request => request.FileName == "git" &&
                           request.Arguments.Count >= 1 &&
                           request.Arguments[0] == "add");
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }

    private static Mock<IApiKeySecretStore> CreateSecretStoreMock()
    {
        Mock<IApiKeySecretStore> secretStore = new(MockBehavior.Strict);
        secretStore
            .Setup(static store => store.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        secretStore
            .Setup(static store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        return secretStore;
    }

    private static Mock<IConversationConfigurationAccessor> CreateConversationConfigurationAccessorMock()
    {
        Mock<IConversationConfigurationAccessor> accessor = new(MockBehavior.Strict);
        accessor
            .Setup(static value => value.GetSettings())
            .Returns(new ConversationSettings(
                SystemPrompt: null,
                RequestTimeout: TimeSpan.FromSeconds(30),
                MaxHistoryTurns: 20,
                MaxToolRoundsPerTurn: 8));
        return accessor;
    }
}
