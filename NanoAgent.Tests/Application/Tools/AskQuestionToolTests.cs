using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using System.Text.Json;

namespace NanoAgent.Tests.Application.Tools;

public sealed class AskQuestionToolTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnInvalidArguments_When_QuestionIsMissing()
    {
        AskQuestionTool sut = new(new QueueSelectionPrompt(), new QueueTextPrompt());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("{}"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.InvalidArguments);
        result.Message.Should().Contain("requires a non-empty 'question'");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnSelectedLabel_For_SingleChoice()
    {
        QueueSelectionPrompt selection = new(1);
        AskQuestionTool sut = new(selection, new QueueTextPrompt());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "question": "Which database?",
                  "options": [
                    { "label": "Postgres" },
                    { "label": "SQLite" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("SQLite");
        result.JsonResult.Should().Contain("\"Answered\":true");
        result.JsonResult.Should().Contain("SQLite");
    }

    [Fact]
    public async Task ExecuteAsync_Should_RouteToTextPrompt_When_OtherIsChosen()
    {
        // The appended "Other…" option carries the sentinel value -2.
        QueueSelectionPrompt selection = new(-2);
        QueueTextPrompt text = new("GraphQL");
        AskQuestionTool sut = new(selection, text);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "question": "Which API style?",
                  "options": [
                    { "label": "REST" },
                    { "label": "gRPC" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("GraphQL");
    }

    [Fact]
    public async Task ExecuteAsync_Should_CollectMultipleAnswers_For_MultiSelect()
    {
        // Each real option carries its original index as its value; "Done" carries -1.
        // Toggle "Auth" (value 0) and "Search" (value 2), then choose Done (value -1).
        QueueSelectionPrompt selection = new(0, 2, -1);
        AskQuestionTool sut = new(selection, new QueueTextPrompt());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "question": "Which features?",
                  "multiSelect": true,
                  "allowFreeText": false,
                  "options": [
                    { "label": "Auth" },
                    { "label": "Billing" },
                    { "label": "Search" }
                  ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.JsonResult.Should().Contain("Auth");
        result.JsonResult.Should().Contain("Search");
        result.JsonResult.Should().NotContain("Billing");
        result.JsonResult.Should().Contain("\"MultiSelect\":true");
    }

    [Fact]
    public async Task ExecuteAsync_Should_UseTextPrompt_When_NoOptionsProvided()
    {
        QueueTextPrompt text = new("Use feature flags.");
        AskQuestionTool sut = new(new QueueSelectionPrompt(), text);

        ToolResult result = await sut.ExecuteAsync(
            CreateContext("""{ "question": "How should we roll this out?" }"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Success);
        result.Message.Should().Contain("Use feature flags.");
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnGracefulError_When_PromptIsCancelled()
    {
        AskQuestionTool sut = new(new CancellingSelectionPrompt(), new QueueTextPrompt());

        ToolResult result = await sut.ExecuteAsync(
            CreateContext(
                """
                {
                  "question": "Which option?",
                  "options": [ { "label": "A" }, { "label": "B" } ]
                }
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.ExecutionError);
        result.JsonResult.Should().Contain("question_unanswered");
    }

    private static ToolExecutionContext CreateContext(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        return new ToolExecutionContext(
            "call_1",
            AgentToolNames.AskQuestion,
            document.RootElement.Clone(),
            TestSessionFactory.Create());
    }

    private sealed class QueueSelectionPrompt : ISelectionPrompt
    {
        private readonly Queue<int> _responses;

        public QueueSelectionPrompt(params int[] responses)
        {
            _responses = new Queue<int>(responses);
        }

        public Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
        {
            int response = _responses.Dequeue();
            SelectionPromptOption<T> match = request.Options.First(option =>
                option.Value is int value && value == response);
            return Task.FromResult(match.Value);
        }
    }

    private sealed class CancellingSelectionPrompt : ISelectionPrompt
    {
        public Task<T> PromptAsync<T>(SelectionPromptRequest<T> request, CancellationToken cancellationToken)
        {
            throw new PromptCancelledException();
        }
    }

    private sealed class QueueTextPrompt : ITextPrompt
    {
        private readonly Queue<string> _responses;

        public QueueTextPrompt(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public Task<string> PromptAsync(TextPromptRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : string.Empty);
        }
    }
}
