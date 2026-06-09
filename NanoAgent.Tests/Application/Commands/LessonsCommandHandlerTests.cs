using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class LessonsCommandHandlerTests : IDisposable
{
    private readonly string _workspacePath;

    public LessonsCommandHandlerTests()
    {
        _workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-lessons-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspacePath);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ShowOffByDefaultStatus()
    {
        LessonsCommandHandler sut = CreateHandler();

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Status: off");
        result.Message.Should().Contain("Automatic prompt injection: off");
    }

    [Fact]
    public async Task ExecuteAsync_Should_EnableLessonMemoryAndPersistSettings()
    {
        MemorySettings memorySettings = new();
        CapturingWorkspaceSettingsWriter workspaceSettingsWriter = new();
        LessonsCommandHandler sut = CreateHandler(
            memorySettings: memorySettings,
            workspaceSettingsWriter: workspaceSettingsWriter);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("on"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        memorySettings.LessonsEnabled.Should().BeTrue();
        memorySettings.AllowAutoFailureObservation.Should().BeFalse();
        workspaceSettingsWriter.WorkspacePath.Should().Be(_workspacePath);
        workspaceSettingsWriter.MemorySettings.Should().NotBeNull();
        workspaceSettingsWriter.MemorySettings!.LessonsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectMutatingOperationsWhileDisabled()
    {
        LessonsCommandHandler sut = CreateHandler();

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("save Trigger | Problem | Lesson"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Warning);
        result.Message.Should().Contain("/lessons on");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ManageLessons_WhenEnabled()
    {
        InMemoryLessonMemoryService lessonMemoryService = new();
        LessonsCommandHandler sut = CreateHandler(
            lessonMemoryService: lessonMemoryService,
            memorySettings: new MemorySettings
            {
                LessonsEnabled = true
            });

        ReplCommandResult saveResult = await sut.ExecuteAsync(
            CreateContext("save CS0246 during build | Missing DI registration | Check ServiceCollectionExtensions first."),
            CancellationToken.None);
        saveResult.Message.Should().Contain("Saved lesson");
        string id = lessonMemoryService.Entries.Single().Id;

        ReplCommandResult listResult = await sut.ExecuteAsync(
            CreateContext("list"),
            CancellationToken.None);
        listResult.Message.Should().Contain(id);
        listResult.Message.Should().Contain("ServiceCollectionExtensions");

        ReplCommandResult searchResult = await sut.ExecuteAsync(
            CreateContext("search CS0246"),
            CancellationToken.None);
        searchResult.Message.Should().Contain(id);

        ReplCommandResult editResult = await sut.ExecuteAsync(
            CreateContext($"edit {id} CS0246 during build | Missing DI registration | Check DI extensions before touching unrelated files."),
            CancellationToken.None);
        editResult.Message.Should().Contain("Edited lesson");
        lessonMemoryService.Entries.Single().Lesson.Should().Contain("unrelated files");

        ReplCommandResult deleteResult = await sut.ExecuteAsync(
            CreateContext($"delete {id}"),
            CancellationToken.None);
        deleteResult.Message.Should().Contain("Deleted lesson");
        lessonMemoryService.Entries.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    private LessonsCommandHandler CreateHandler(
        ILessonMemoryService? lessonMemoryService = null,
        MemorySettings? memorySettings = null,
        IWorkspaceSettingsWriter? workspaceSettingsWriter = null)
    {
        return new LessonsCommandHandler(
            lessonMemoryService ?? new InMemoryLessonMemoryService(),
            memorySettings ?? new MemorySettings(),
            workspaceSettingsWriter ?? new CapturingWorkspaceSettingsWriter());
    }

    private ReplCommandContext CreateContext(string argumentText)
    {
        string[] arguments = string.IsNullOrWhiteSpace(argumentText)
            ? []
            : argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ReplCommandContext(
            "lessons",
            argumentText,
            arguments,
            string.IsNullOrWhiteSpace(argumentText) ? "/lessons" : $"/lessons {argumentText}",
            new ReplSessionContext(
                new AgentProviderProfile(ProviderKind.OpenAi, null),
                "gpt-4.1",
                ["gpt-4.1"],
                workspacePath: _workspacePath));
    }

    private sealed class CapturingWorkspaceSettingsWriter : IWorkspaceSettingsWriter
    {
        public MemorySettings? MemorySettings { get; private set; }

        public string? WorkspacePath { get; private set; }

        public Task SavePermissionSettingsAsync(
            string workspacePath,
            PermissionSettings settings,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SaveMemorySettingsAsync(
            string workspacePath,
            MemorySettings settings,
            CancellationToken cancellationToken)
        {
            WorkspacePath = workspacePath;
            MemorySettings = new MemorySettings
            {
                AllowAutoFailureObservation = settings.AllowAutoFailureObservation,
                AllowAutoManualLessons = settings.AllowAutoManualLessons,
                Disabled = settings.Disabled,
                LessonsEnabled = settings.LessonsEnabled,
                MaxEntries = settings.MaxEntries,
                MaxPromptChars = settings.MaxPromptChars,
                RedactSecrets = settings.RedactSecrets,
                RequireApprovalForWrites = settings.RequireApprovalForWrites
            };
            return Task.CompletedTask;
        }

        public Task SaveTelemetryEnabledAsync(
            string workspacePath,
            bool enabled,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryLessonMemoryService : ILessonMemoryService
    {
        public List<LessonMemoryEntry> Entries { get; } = [];

        public Task<string?> CreatePromptAsync(
            string query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken)
        {
            LessonMemoryEntry? entry = Entries.SingleOrDefault(item => item.Id == id);
            if (entry is null)
            {
                return Task.FromResult(false);
            }

            Entries.Remove(entry);
            return Task.FromResult(true);
        }

        public Task<LessonMemoryEntry?> EditAsync(
            LessonMemoryEditRequest request,
            CancellationToken cancellationToken)
        {
            LessonMemoryEntry? entry = Entries.SingleOrDefault(item => item.Id == request.Id);
            if (entry is null)
            {
                return Task.FromResult<LessonMemoryEntry?>(null);
            }

            LessonMemoryEntry updated = entry with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Trigger = request.Trigger ?? entry.Trigger,
                Problem = request.Problem ?? entry.Problem,
                Lesson = request.Lesson ?? entry.Lesson
            };
            Entries[Entries.IndexOf(entry)] = updated;
            return Task.FromResult<LessonMemoryEntry?>(updated);
        }

        public string GetStoragePath()
        {
            return ".nanoagent/memory/lessons.jsonl";
        }

        public Task<IReadOnlyList<LessonMemoryEntry>> ListAsync(
            int limit,
            bool includeFixed,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LessonMemoryEntry>>(Entries.Take(limit).ToArray());
        }

        public Task ObserveToolResultAsync(
            ConversationToolCall toolCall,
            ToolInvocationResult invocationResult,
            CancellationToken cancellationToken,
            ReplSessionContext? session = null)
        {
            return Task.CompletedTask;
        }

        public Task<LessonMemoryEntry> SaveAsync(
            LessonMemorySaveRequest request,
            CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LessonMemoryEntry entry = new(
                $"les_{Entries.Count + 1}",
                now,
                now,
                request.Kind,
                request.Trigger,
                request.Problem,
                request.Lesson,
                []);
            Entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<IReadOnlyList<LessonMemoryEntry>> SearchAsync(
            string query,
            int limit,
            bool includeFixed,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<LessonMemoryEntry> results = Entries
                .Where(entry =>
                    entry.Trigger.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Problem.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Lesson.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray();
            return Task.FromResult(results);
        }
    }
}
