using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal sealed class AskQuestionTool : ITool
{
    private const int DoneOptionValue = -1;
    private const int OtherOptionValue = -2;

    private readonly ISelectionPrompt _selectionPrompt;
    private readonly ITextPrompt _textPrompt;

    public AskQuestionTool(
        ISelectionPrompt selectionPrompt,
        ITextPrompt textPrompt)
    {
        _selectionPrompt = selectionPrompt ?? throw new ArgumentNullException(nameof(selectionPrompt));
        _textPrompt = textPrompt ?? throw new ArgumentNullException(nameof(textPrompt));
    }

    public string Description =>
        "Ask the user a question and wait for their answer before continuing. Use this when you are blocked on a decision that is genuinely the user's to make and cannot be resolved from the request, the code, or sensible defaults: choosing between real alternatives, confirming intent, or clarifying scope. Provide 'options' for a multiple-choice question (an 'Other' choice lets the user type a custom answer), set 'multiSelect' to allow several answers, or omit 'options' for a free-form question. In plan mode, use this to clarify requirements or pick between approaches before finalizing the plan. Do not use it for trivial choices, for facts you can verify yourself, or once you have enough information to act.";

    public string Name => AgentToolNames.AskQuestion;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "bypassUserPermissionRules": true,
          "toolTags": ["interactive"]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "question": {
              "type": "string",
              "description": "The question to ask the user. Be clear and specific."
            },
            "header": {
              "type": "string",
              "description": "Optional very short label (a few words) shown as the prompt title."
            },
            "options": {
              "type": "array",
              "description": "Choices to present. Provide at least two for a multiple-choice question, or omit for a free-form text question.",
              "items": {
                "type": "object",
                "properties": {
                  "label": {
                    "type": "string",
                    "description": "The choice text shown to the user."
                  },
                  "description": {
                    "type": "string",
                    "description": "Optional explanation of what this choice means."
                  }
                },
                "required": ["label"],
                "additionalProperties": false
              }
            },
            "multiSelect": {
              "type": "boolean",
              "description": "Allow the user to select multiple options. Defaults to false. Ignored for free-form questions."
            },
            "allowFreeText": {
              "type": "boolean",
              "description": "Add an 'Other' choice that lets the user type a custom answer. Defaults to true. Ignored for free-form questions."
            }
          },
          "required": ["question"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "question", out string? question))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_question",
                "Tool 'ask_question' requires a non-empty 'question' string.",
                new ToolRenderPayload(
                    "Invalid ask_question arguments",
                    "Provide a non-empty 'question' string."));
        }

        string? header = ToolArguments.GetOptionalString(context.Arguments, "header");
        bool multiSelect = ToolArguments.GetBoolean(context.Arguments, "multiSelect");
        bool allowFreeText = ToolArguments.GetBoolean(context.Arguments, "allowFreeText", defaultValue: true);
        IReadOnlyList<QuestionOption> options = ParseOptions(context.Arguments);

        try
        {
            IReadOnlyList<string> answers = options.Count == 0
                ? [await AskFreeTextAsync(question!, header, cancellationToken)]
                : multiSelect
                    ? await AskMultipleChoiceAsync(question!, header, options, allowFreeText, cancellationToken)
                    : [await AskSingleChoiceAsync(question!, header, options, allowFreeText, cancellationToken)];

            answers = answers
                .Where(static answer => !string.IsNullOrWhiteSpace(answer))
                .ToArray();

            AskQuestionResult result = new(
                question!,
                header,
                multiSelect,
                Answered: answers.Count > 0,
                answers);

            return ToolResultFactory.Success(
                answers.Count == 0
                    ? "The user submitted no answer."
                    : $"User answered: {string.Join(", ", answers)}",
                result,
                ToolJsonContext.Default.AskQuestionResult,
                new ToolRenderPayload(
                    string.IsNullOrWhiteSpace(header) ? "Question" : header!.Trim(),
                    BuildRenderText(question!, answers)));
        }
        catch (PromptCancelledException)
        {
            // The user dismissed the prompt, or no interactive surface is available
            // (for example a one-shot/headless run with redirected stdin). Surface a
            // graceful result so the model can continue with its best judgment instead
            // of failing the turn.
            return ToolResultFactory.ExecutionError(
                "question_unanswered",
                "The question was not answered (the user dismissed it or no interactive user is available). Proceed using your best judgment and reasonable assumptions, and state any assumption you make.",
                new ToolRenderPayload(
                    "Question not answered",
                    "No answer was provided; continuing without user input."));
        }
    }

    private Task<string> AskFreeTextAsync(
        string question,
        string? header,
        CancellationToken cancellationToken)
    {
        return _textPrompt.PromptAsync(
            new TextPromptRequest(
                string.IsNullOrWhiteSpace(header) ? question : header!.Trim(),
                string.IsNullOrWhiteSpace(header) ? null : question,
                DefaultValue: null,
                AllowCancellation: true),
            cancellationToken);
    }

    private async Task<string> AskSingleChoiceAsync(
        string question,
        string? header,
        IReadOnlyList<QuestionOption> options,
        bool allowFreeText,
        CancellationToken cancellationToken)
    {
        List<SelectionPromptOption<int>> selectionOptions = [];
        for (int index = 0; index < options.Count; index++)
        {
            selectionOptions.Add(new SelectionPromptOption<int>(
                options[index].Label,
                index,
                options[index].Description));
        }

        if (allowFreeText)
        {
            selectionOptions.Add(new SelectionPromptOption<int>(
                "Other…",
                OtherOptionValue,
                "Type a custom answer."));
        }

        int choice = await _selectionPrompt.PromptAsync(
            new SelectionPromptRequest<int>(
                BuildTitle(question, header),
                selectionOptions,
                string.IsNullOrWhiteSpace(header) ? null : question,
                DefaultIndex: 0,
                AllowCancellation: true,
                AutoSelectAfter: null),
            cancellationToken);

        if (choice == OtherOptionValue)
        {
            return await AskFreeTextAsync(question, "Your answer", cancellationToken);
        }

        return options[choice].Label;
    }

    private async Task<IReadOnlyList<string>> AskMultipleChoiceAsync(
        string question,
        string? header,
        IReadOnlyList<QuestionOption> options,
        bool allowFreeText,
        CancellationToken cancellationToken)
    {
        HashSet<int> selected = [];
        List<string> freeTextAnswers = [];
        string title = BuildTitle(question, header);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<SelectionPromptOption<int>> selectionOptions =
            [
                new SelectionPromptOption<int>(
                    "Done",
                    DoneOptionValue,
                    selected.Count == 0 && freeTextAnswers.Count == 0
                        ? "Finish without selecting anything."
                        : $"Finish and submit {selected.Count + freeTextAnswers.Count} answer(s).")
            ];

            for (int index = 0; index < options.Count; index++)
            {
                string marker = selected.Contains(index) ? "[x] " : "[ ] ";
                selectionOptions.Add(new SelectionPromptOption<int>(
                    marker + options[index].Label,
                    index,
                    options[index].Description));
            }

            if (allowFreeText)
            {
                selectionOptions.Add(new SelectionPromptOption<int>(
                    "Other…",
                    OtherOptionValue,
                    "Type a custom answer and add it to your selection."));
            }

            int choice = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<int>(
                    title,
                    selectionOptions,
                    string.IsNullOrWhiteSpace(header) ? null : question,
                    DefaultIndex: 0,
                    AllowCancellation: true,
                    AutoSelectAfter: null),
                cancellationToken);

            if (choice == DoneOptionValue)
            {
                break;
            }

            if (choice == OtherOptionValue)
            {
                string typed = await AskFreeTextAsync(question, "Your answer", cancellationToken);
                if (!string.IsNullOrWhiteSpace(typed))
                {
                    freeTextAnswers.Add(typed);
                }

                continue;
            }

            if (!selected.Remove(choice))
            {
                selected.Add(choice);
            }
        }

        List<string> answers = [];
        for (int index = 0; index < options.Count; index++)
        {
            if (selected.Contains(index))
            {
                answers.Add(options[index].Label);
            }
        }

        answers.AddRange(freeTextAnswers);
        return answers;
    }

    private static string BuildTitle(string question, string? header)
    {
        return string.IsNullOrWhiteSpace(header)
            ? question
            : header!.Trim();
    }

    private static string BuildRenderText(string question, IReadOnlyList<string> answers)
    {
        StringBuilder builder = new();
        builder.Append("Q: ").Append(question.Trim());

        if (answers.Count == 0)
        {
            builder.Append(Environment.NewLine).Append("A: (no answer)");
            return builder.ToString();
        }

        foreach (string answer in answers)
        {
            builder.Append(Environment.NewLine).Append("A: ").Append(answer.Trim());
        }

        return builder.ToString();
    }

    private static IReadOnlyList<QuestionOption> ParseOptions(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("options", out JsonElement optionsElement) ||
            optionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<QuestionOption> options = [];
        foreach (JsonElement item in optionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!ToolArguments.TryGetNonEmptyString(item, "label", out string? label))
            {
                continue;
            }

            options.Add(new QuestionOption(
                label!,
                ToolArguments.GetOptionalString(item, "description")));
        }

        return options;
    }

    private sealed record QuestionOption(string Label, string? Description);
}
