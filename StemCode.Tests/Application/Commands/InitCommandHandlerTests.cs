using FluentAssertions;
using StemCode.Application.Abstractions;
using StemCode.Application.Commands;
using StemCode.Application.Models;
using StemCode.Domain.Models;

namespace StemCode.Tests.Application.Commands;

public sealed class InitCommandHandlerTests : IDisposable
{
    private readonly string _workspaceRoot;

    public InitCommandHandlerTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"StemCode-Init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_CreateMinimalFiles_When_MinimalPresetArgumentIsUsed()
    {
        InitCommandHandler sut = new(
            new ThrowingSelectionPrompt(),
            new ThrowingConfirmationPrompt());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("minimal"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Preset: Minimal");
        AssertExists(".stemcode/agent-profile.json");
        AssertExists(".stemcode/README.md");
        AssertExists(".stemcode/.gitignore");
        AssertExists(".stemcode/.stemcodeignore");
        AssertNotExists(".stemcode/memory");
        AssertNotExists(".stemcode/agents");
        AssertNotExists(".stemcode/skills");
        AssertNotExists(".stemcode/cache");
        AssertNotExists(".stemcode/logs");
        AssertNotExists(".stemcode/SystemPrompt.md.template");
    }

    [Fact]
    public async Task ExecuteAsync_Should_CreateRecommendedFilesWithoutSystemPromptTemplate()
    {
        InitCommandHandler sut = new(
            new ThrowingSelectionPrompt(),
            new ThrowingConfirmationPrompt());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("recommended"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Preset: Recommended");
        AssertExists(".stemcode/agent-profile.json");
        AssertExists(".stemcode/agents/code-reviewer.md.template");
        AssertExists(".stemcode/skills/dotnet/SKILL.md.template");
        AssertExists(".stemcode/cache");
        AssertExists(".stemcode/logs/.gitkeep");
        AssertExists(".stemcode/memory/architecture.md");
        AssertExists(".stemcode/memory/conventions.md");
        AssertExists(".stemcode/memory/decisions.md");
        AssertExists(".stemcode/memory/known-issues.md");
        AssertExists(".stemcode/memory/test-strategy.md");
        AssertExists(".stemcode/memory/lessons.jsonl");
        AssertNotExists(".stemcode/SystemPrompt.md.template");
    }

    [Fact]
    public async Task ExecuteAsync_Should_PromptForPreset_When_NoArgumentIsUsed()
    {
        LabelSelectionPrompt selectionPrompt = new("Minimal");
        InitCommandHandler sut = new(
            selectionPrompt,
            new ThrowingConfirmationPrompt());

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(argumentText: string.Empty),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Preset: Minimal");
        selectionPrompt.RequestTitles.Should().ContainSingle("Choose workspace files to add");
        AssertExists(".stemcode/agent-profile.json");
        AssertNotExists(".stemcode/memory");
    }

    [Fact]
    public async Task ExecuteAsync_Should_CreateSystemPromptTemplate_When_CustomChoiceSelectsIt()
    {
        AnsweringConfirmationPrompt confirmationPrompt = new(request =>
            !request.Title.Contains("repo memory", StringComparison.OrdinalIgnoreCase) ||
            request.Title.Contains("SystemPrompt", StringComparison.OrdinalIgnoreCase));
        InitCommandHandler sut = new(
            new ThrowingSelectionPrompt(),
            confirmationPrompt);

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext("custom"),
            CancellationToken.None);

        result.FeedbackKind.Should().Be(ReplFeedbackKind.Info);
        result.Message.Should().Contain("Preset: Custom");
        confirmationPrompt.RequestTitles.Should().Contain("Add inactive SystemPrompt template?");
        AssertExists(".stemcode/SystemPrompt.md.template");
        AssertExists(".stemcode/memory/lessons.jsonl");
        AssertNotExists(".stemcode/memory/architecture.md");
        AssertNotExists(".stemcode/SystemPrompt.md");
    }

    private ReplCommandContext CreateContext(string argumentText)
    {
        ReplSessionContext session = new(
            new AgentProviderProfile(ProviderKind.OpenAi, BaseUrl: null),
            "gpt-4.1",
            ["gpt-4.1"],
            workspacePath: _workspaceRoot);

        string[] arguments = string.IsNullOrWhiteSpace(argumentText)
            ? []
            : argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ReplCommandContext(
            "init",
            argumentText,
            arguments,
            string.IsNullOrWhiteSpace(argumentText) ? "/init" : $"/init {argumentText}",
            session);
    }

    private void AssertExists(string relativePath)
    {
        Path.Exists(Path.Combine(_workspaceRoot, relativePath)).Should().BeTrue(relativePath);
    }

    private void AssertNotExists(string relativePath)
    {
        Path.Exists(Path.Combine(_workspaceRoot, relativePath)).Should().BeFalse(relativePath);
    }

    private sealed class ThrowingSelectionPrompt : ISelectionPrompt
    {
        public Task<T> PromptAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected selection prompt.");
        }
    }

    private sealed class LabelSelectionPrompt : ISelectionPrompt
    {
        private readonly string _label;

        public LabelSelectionPrompt(string label)
        {
            _label = label;
        }

        public List<string> RequestTitles { get; } = [];

        public Task<T> PromptAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            RequestTitles.Add(request.Title);
            SelectionPromptOption<T> option = request.Options.Single(candidate =>
                string.Equals(candidate.Label, _label, StringComparison.Ordinal));
            return Task.FromResult(option.Value);
        }
    }

    private sealed class ThrowingConfirmationPrompt : IConfirmationPrompt
    {
        public Task<bool> PromptAsync(
            ConfirmationPromptRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected confirmation prompt.");
        }
    }

    private sealed class AnsweringConfirmationPrompt : IConfirmationPrompt
    {
        private readonly Func<ConfirmationPromptRequest, bool> _answer;

        public AnsweringConfirmationPrompt(Func<ConfirmationPromptRequest, bool> answer)
        {
            _answer = answer;
        }

        public List<string> RequestTitles { get; } = [];

        public Task<bool> PromptAsync(
            ConfirmationPromptRequest request,
            CancellationToken cancellationToken)
        {
            RequestTitles.Add(request.Title);
            return Task.FromResult(_answer(request));
        }
    }
}
