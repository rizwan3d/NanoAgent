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
            EnqueueSuccessfulAutoCommit(processRunner, workspacePath);

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
    public async Task TryAutoCommitAsync_Should_Refuse_When_AnyChangesAreAlreadyStaged()
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
            processRunner.EnqueueResult(new ProcessExecutionResult(0, "src/test.txt", string.Empty));

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

    [Fact]
    public async Task TryAutoCommitAsync_Should_StagePaths_RelativeToRepositoryRoot_WhenWorkspaceIsSubdirectory()
    {
        string repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        string workspacePath = Path.Combine(repositoryRoot, "src", "app");
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(processRunner, repositoryRoot);

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
                [new SessionEditContext(DateTimeOffset.UtcNow, "edit", ["Program.cs"], 1, 0)],
                CancellationToken.None);

            ProcessExecutionRequest addRequest = processRunner.Requests.Should().ContainSingle(
                request => request.FileName == "git" &&
                           request.Arguments.Count >= 1 &&
                           request.Arguments[0] == "add").Subject;

            addRequest.WorkingDirectory.Should().Be(repositoryRoot);
            addRequest.Arguments.Should().Equal("add", "-A", "--", "src/app/Program.cs");
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryAutoCommitAsync_Should_StageBothPaths_WhenEditRecordsRename()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(processRunner, workspacePath);

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
                [new SessionEditContext(DateTimeOffset.UtcNow, "apply_patch (move old.txt -> new.txt)", ["old.txt -> new.txt"], 1, 1)],
                CancellationToken.None);

            ProcessExecutionRequest addRequest = processRunner.Requests.Should().ContainSingle(
                request => request.FileName == "git" &&
                           request.Arguments.Count >= 1 &&
                           request.Arguments[0] == "add").Subject;

            addRequest.Arguments.Should().Equal("add", "-A", "--", "old.txt", "new.txt");
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
    public async Task TryAutoCommitAsync_Should_ResetNormalIndexAfterTemporaryIndexCommit()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(processRunner, workspacePath);

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

            ProcessExecutionRequest resetRequest = processRunner.Requests.Should().ContainSingle(
                request => request.FileName == "git" &&
                           request.Arguments.Count == 3 &&
                           request.Arguments[0] == "reset" &&
                           request.Arguments[1] == "--mixed" &&
                           request.Arguments[2] == "HEAD").Subject;

            resetRequest.EnvironmentVariables.Should().BeNull();
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
    public async Task TryAutoCommitAsync_Should_Abort_When_HeadChangesBeforeCommit()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(
                processRunner,
                workspacePath,
                currentHeadSha: "head-2");

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

            processRunner.Requests.Should().NotContain(request =>
                request.FileName == "git" &&
                request.Arguments.Count >= 1 &&
                request.Arguments[0] == "commit");
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
    public async Task TryAutoCommitAsync_Should_Abort_When_BranchChangesBeforeCommit()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(
                processRunner,
                workspacePath,
                currentBranchReference: "refs/heads/other");

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

            processRunner.Requests.Should().NotContain(request =>
                request.FileName == "git" &&
                request.Arguments.Count >= 1 &&
                request.Arguments[0] == "commit");
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
    public async Task TryAutoCommitAsync_Should_SkipReset_When_NormalIndexChangesAfterCommit()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(
                processRunner,
                workspacePath,
                postCommitIndexTree: "index-tree-2");

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

            processRunner.Requests.Should().Contain(request =>
                request.FileName == "git" &&
                request.Arguments.Count >= 1 &&
                request.Arguments[0] == "commit");
            processRunner.Requests.Should().NotContain(request =>
                request.FileName == "git" &&
                request.Arguments.Count >= 1 &&
                request.Arguments[0] == "reset");
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
    public async Task TryAutoCommitAsync_Should_Abort_When_TrackedFileContentNoLongerMatchesAgentEdit()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-autocommit-service-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        string filePath = Path.Combine(workspacePath, "src");
        Directory.CreateDirectory(filePath);
        File.WriteAllText(Path.Combine(filePath, "test.txt"), "user change");

        try
        {
            FakeProcessRunner processRunner = new();
            EnqueueSuccessfulAutoCommit(processRunner, workspacePath);

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
            session.RecordFileEditTransaction(new WorkspaceFileEditTransaction(
                "file_write (src/test.txt)",
                [new WorkspaceFileEditState("src/test.txt", exists: true, content: "before")],
                [new WorkspaceFileEditState("src/test.txt", exists: true, content: "agent change")]));

            await sut.TryAutoCommitAsync(
                session,
                [new SessionEditContext(DateTimeOffset.UtcNow, "edit", ["src/test.txt"], 1, 0)],
                CancellationToken.None);

            processRunner.Requests.Should().NotContain(request =>
                request.FileName == "git" &&
                request.Arguments.Count >= 1 &&
                request.Arguments[0] == "commit");
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

    private static void EnqueueSuccessfulAutoCommit(
        FakeProcessRunner processRunner,
        string repositoryRoot,
        string initialHeadSha = "head-1",
        string initialBranchReference = "refs/heads/main",
        string? currentHeadSha = null,
        string? currentBranchReference = null,
        string initialIndexTree = "index-tree-1",
        string? currentIndexTree = null,
        string? postCommitIndexTree = null)
    {
        processRunner.EnqueueResult(new ProcessExecutionResult(0, "true", string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, repositoryRoot, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, initialIndexTree, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, initialHeadSha, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, initialBranchReference, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, initialHeadSha, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(1, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, currentHeadSha ?? initialHeadSha, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, currentBranchReference ?? initialBranchReference, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, currentIndexTree ?? initialIndexTree, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, postCommitIndexTree ?? currentIndexTree ?? initialIndexTree, string.Empty));
        processRunner.EnqueueResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
    }
}
