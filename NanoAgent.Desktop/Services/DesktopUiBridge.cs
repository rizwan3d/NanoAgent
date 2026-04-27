using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using NanoAgent.Desktop.Models;

namespace NanoAgent.Desktop.Services;

internal sealed class DesktopUiBridge : IUiBridge
{
    private readonly IPlanOutputFormatter _planOutputFormatter;
    private readonly Action<string, string> _addConversationMessage;
    private readonly Action<DesktopSelectionPrompt?> _setSelectionPrompt;
    private readonly Action<DesktopTextPrompt?> _setTextPrompt;
    private readonly IToolOutputFormatter _toolOutputFormatter;
    private readonly Action<string> _addActivity;
    private DesktopSelectionPrompt? _currentSelectionPrompt;
    private DesktopTextPrompt? _currentTextPrompt;

    public DesktopUiBridge(
        Action<string> addActivity,
        Action<string, string> addConversationMessage,
        Action<DesktopSelectionPrompt?> setSelectionPrompt,
        Action<DesktopTextPrompt?> setTextPrompt)
    {
        _addActivity = addActivity ?? throw new ArgumentNullException(nameof(addActivity));
        _addConversationMessage = addConversationMessage ?? throw new ArgumentNullException(nameof(addConversationMessage));
        _setSelectionPrompt = setSelectionPrompt ?? throw new ArgumentNullException(nameof(setSelectionPrompt));
        _setTextPrompt = setTextPrompt ?? throw new ArgumentNullException(nameof(setTextPrompt));
        _toolOutputFormatter = new ToolOutputFormatter();
        _planOutputFormatter = new PlanOutputFormatter();
    }

    public async Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.Count == 0)
        {
            throw new InvalidOperationException("NanoAgent requested a selection with no available options.");
        }

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DesktopSelectionPrompt? prompt = null;

        prompt = new DesktopSelectionPrompt(
            request.Title,
            request.Description,
            request.Options
                .Select(static option => new DesktopSelectionPromptOptionDescriptor(
                    option.Label,
                    option.Description))
                .ToArray(),
            request.DefaultIndex,
            request.AllowCancellation,
            request.AutoSelectAfter,
            onSelected: (selectedIndex, isAutomatic) =>
            {
                SelectionPromptOption<T> selectedOption = request.Options[selectedIndex];
                string prefix = isAutomatic ? "Auto-selected" : "Selected";
                AddActivity($"{prefix} '{selectedOption.Label}' for: {request.Title}");
                completion.TrySetResult(selectedOption.Value);
            },
            onCancelled: () =>
            {
                AddActivity($"Cancelled prompt: {request.Title}");
                completion.TrySetException(new PromptCancelledException());
            });

        prompt.Dismissed += (_, _) => ClearSelectionPrompt(prompt);
        SetSelectionPrompt(prompt);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            prompt.Dismiss();
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            ClearSelectionPrompt(prompt);
        }
    }

    public async Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DesktopTextPrompt? prompt = null;

        prompt = new DesktopTextPrompt(
            request.Label,
            request.Description,
            request.DefaultValue,
            request.AllowCancellation,
            isSecret,
            onSubmitted: value =>
            {
                AddActivity($"Submitted prompt: {request.Label}");
                completion.TrySetResult(value);
            },
            onCancelled: () =>
            {
                AddActivity($"Cancelled prompt: {request.Label}");
                completion.TrySetException(new PromptCancelledException());
            });

        prompt.Dismissed += (_, _) => ClearTextPrompt(prompt);
        SetTextPrompt(prompt);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            prompt.Dismiss();
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            ClearTextPrompt(prompt);
        }
    }

    public void ShowError(string message)
    {
        AddActivity($"Error: {message}");
    }

    public void ShowInfo(string message)
    {
        AddActivity(message);
    }

    public void ShowSuccess(string message)
    {
        AddActivity($"Success: {message}");
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        string[] descriptions = toolCalls
            .Select(_toolOutputFormatter.DescribeCall)
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        if (descriptions.Length == 0)
        {
            AddActivity("Running tools");
            AddConversationMessage("Tool", "Running tools");
            return;
        }

        string message = descriptions.Length == 1
            ? $"Running {descriptions[0]}"
            : "Running tools:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                descriptions.Select(static description => $"- {description}"));

        AddActivity(message);
        AddConversationMessage("Tool", message);
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        foreach (string message in _toolOutputFormatter.FormatResults(toolExecutionResult))
        {
            AddActivity(message);
            AddConversationMessage("Tool", message);
        }
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        string message = _planOutputFormatter.Format(progress);
        AddActivity(message);
        AddConversationMessage("Plan", message);
    }

    private void AddActivity(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _addActivity(message.Trim());
        }
    }

    private void AddConversationMessage(string role, string message)
    {
        if (!string.IsNullOrWhiteSpace(role) &&
            !string.IsNullOrWhiteSpace(message))
        {
            _addConversationMessage(role.Trim(), message.Trim());
        }
    }

    private void SetSelectionPrompt(DesktopSelectionPrompt prompt)
    {
        _currentSelectionPrompt = prompt;
        _setSelectionPrompt(prompt);
    }

    private void ClearSelectionPrompt(DesktopSelectionPrompt prompt)
    {
        if (!ReferenceEquals(_currentSelectionPrompt, prompt))
        {
            return;
        }

        _currentSelectionPrompt = null;
        _setSelectionPrompt(null);
    }

    private void SetTextPrompt(DesktopTextPrompt prompt)
    {
        _currentTextPrompt = prompt;
        _setTextPrompt(prompt);
    }

    private void ClearTextPrompt(DesktopTextPrompt prompt)
    {
        if (!ReferenceEquals(_currentTextPrompt, prompt))
        {
            return;
        }

        _currentTextPrompt = null;
        _setTextPrompt(null);
    }
}
