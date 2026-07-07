using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NanoAgent.CLI;

public sealed class ConsoleBridge : IUiBridge
{
    private readonly IAnsiConsole _console;
    private readonly TextReader _fallbackInput;
    private readonly IPlanOutputFormatter _planOutputFormatter;
    private readonly IToolOutputFormatter _toolOutputFormatter;
    private readonly object _providerAuthKeySync = new();
    private string? _providerAuthKey;
    private bool _providerAuthKeyConsumed;

    public ConsoleBridge(string? providerAuthKey = null)
        : this(
            CreateErrorConsole(),
            Console.In,
            new ToolOutputFormatter(),
            new PlanOutputFormatter(),
            providerAuthKey)
    {
    }

    internal ConsoleBridge(
        IAnsiConsole console,
        TextReader fallbackInput,
        IToolOutputFormatter toolOutputFormatter,
        IPlanOutputFormatter planOutputFormatter,
        string? providerAuthKey = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _fallbackInput = fallbackInput ?? throw new ArgumentNullException(nameof(fallbackInput));
        _toolOutputFormatter = toolOutputFormatter ?? throw new ArgumentNullException(nameof(toolOutputFormatter));
        _planOutputFormatter = planOutputFormatter ?? throw new ArgumentNullException(nameof(planOutputFormatter));
        _providerAuthKey = NormalizeOrNull(providerAuthKey);
    }

    public async Task<T> RequestSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Options.Count == 0)
        {
            throw new PromptCancelledException("No prompt options were available.");
        }

        // Use interactive SelectionPrompt when the terminal supports full interaction,
        // stdin is not redirected, and there is no auto-select timeout (which
        // SelectionPrompt does not support natively).
        if (_console.Profile.Capabilities.Interactive &&
            !Console.IsInputRedirected &&
            request.AutoSelectAfter is null)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                T result = RunInteractiveSelection(request);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }, cancellationToken);
        }

        // Fallback: numbered-list selection for non-interactive or timed-out prompts.
        return await RequestSelectionFallbackAsync(request, cancellationToken);
    }

    public async Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryConsumeProviderAuthKey(request, isSecret, out string providerAuthKey))
        {
            return providerAuthKey;
        }

        // Use interactive TextPrompt when the terminal supports full interaction
        // and stdin is not redirected.
        if (_console.Profile.Capabilities.Interactive && !Console.IsInputRedirected)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string result = RunInteractiveTextPrompt(request, isSecret);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(result) && request.DefaultValue is not null)
                {
                    return request.DefaultValue;
                }

                if (result is null)
                {
                    throw new PromptCancelledException($"No value was provided for {request.Label}.");
                }

                return result;
            }, cancellationToken);
        }

        // Fallback for non-interactive mode (e.g. piped input).
        return await RequestTextFallbackAsync(request, isSecret, cancellationToken);
    }

    public void ShowError(string message)
    {
        WriteColoredStatus("Error", message, "red");
    }

    public void ShowInfo(string message)
    {
        WriteColoredStatus("Info", message, "blue");
    }

    public void ShowSuccess(string message)
    {
        WriteColoredStatus("Success", message, "green");
    }

    public void ShowAssistantReasoning(string reasoningText)
    {
        if (string.IsNullOrWhiteSpace(reasoningText))
        {
            return;
        }

        _console.Write(new Panel(Markup.Escape(reasoningText.Trim()))
            .Header("[bold yellow]Thinking[/]")
            .BorderColor(Color.Yellow));
        _console.WriteLine();
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        string[] descriptions = toolCalls
            .Select(_toolOutputFormatter.DescribeCall)
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        if (descriptions.Length == 0)
        {
            _console.MarkupLine("[bold]Tools[/]: Running tools.");
            return;
        }

        _console.MarkupLine("[bold]Tools[/]: Running:");
        foreach (string description in descriptions)
        {
            _console.MarkupLine($"  - {Markup.Escape(description)}");
        }
    }

    public void ShowToolResults(ToolExecutionBatchResult toolExecutionResult)
    {
        IReadOnlyList<string> messages = _toolOutputFormatter.FormatResults(toolExecutionResult);
        foreach (string message in messages)
        {
            WriteBlock(message);
        }
    }

    public void ShowExecutionPlan(ExecutionPlanProgress progress)
    {
        WriteBlock(_planOutputFormatter.Format(progress));
    }

    public void ShowProviderRetry(ProviderRetryProgress progress)
    {
        if (string.IsNullOrWhiteSpace(progress.Reason))
        {
            return;
        }

        _console.MarkupLine(
            "[bold yellow]Retry[/]: " +
            Markup.Escape($"Provider unreachable ({progress.Reason}). Trying {progress.Attempt}/{progress.MaxAttempts}."));
    }

    private T RunInteractiveSelection<T>(SelectionPromptRequest<T> request)
    {
        var prompt = new SelectionPrompt<T>()
            .Title(Markup.Escape(request.Title))
            .PageSize(10)
            .HighlightStyle(new Style(Color.Cyan1))
            .UseConverter(value => FindLabel(request.Options, value) ?? value?.ToString() ?? "?");

        IReadOnlyList<SelectionPromptOption<T>> options = request.Options;
        bool hasSections = options.Any(static o => !string.IsNullOrWhiteSpace(o.Section));

        // SelectionPrompt<T> in Spectre.Console v0.54.0 does not support AddChoiceGroup
        // or Select. Reorder options so the default is first (pre-selected), and prefix
        // section info to labels when sections are present.
        int defaultIndex = Math.Clamp(request.DefaultIndex, 0, options.Count - 1);

        // Build ordered values with the default item moved to the front.
        List<T> orderedValues = [];

        // Add the default item first (pre-selected position).
        orderedValues.Add(options[defaultIndex].Value);

        // Add every other item in original relative order.
        for (int i = 0; i < options.Count; i++)
        {
            if (i != defaultIndex)
            {
                orderedValues.Add(options[i].Value);
            }
        }

        // When sections are present, build a converter that includes the section prefix
        // so choices remain distinguishable.
        if (hasSections)
        {
            var labelMap = new Dictionary<T, string>();
            foreach (SelectionPromptOption<T> option in options)
            {
                string section = string.IsNullOrWhiteSpace(option.Section)
                    ? string.Empty
                    : option.Section.Trim() + " / ";
                labelMap[option.Value] = section + option.Label;
            }

            prompt.UseConverter(value => labelMap.GetValueOrDefault(value, value?.ToString() ?? "?"));
        }

        prompt.AddChoices(orderedValues);

        return _console.Prompt(prompt);
    }

    private string RunInteractiveTextPrompt(TextPromptRequest request, bool isSecret)
    {
        TextPrompt<string> prompt = new TextPrompt<string>(Markup.Escape(request.Label))
            .AllowEmpty();

        if (isSecret)
        {
            prompt = prompt.Secret('*');
        }

        if (!string.IsNullOrEmpty(request.DefaultValue))
        {
            prompt = prompt
                .DefaultValue(request.DefaultValue)
                .ShowDefaultValue();
        }

        return _console.Prompt(prompt);
    }

    private async Task<T> RequestSelectionFallbackAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken)
    {
        int defaultIndex = Math.Clamp(request.DefaultIndex, 0, request.Options.Count - 1);

        if (Console.IsInputRedirected)
        {
            throw new PromptCancelledException(
                $"Prompt '{request.Title}' requires interactive input.");
        }

        WriteSelectionPromptFallback(request, defaultIndex);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _console.Markup($"[bold]Select [{defaultIndex + 1}][/]: ");

            string? rawValue = await ReadLineWithTimeoutAsync(
                request.AutoSelectAfter,
                cancellationToken);

            if (rawValue is null)
            {
                _console.MarkupLine(
                    "[dim]Using default:[/] " +
                    Markup.Escape(request.Options[defaultIndex].Label));
                return request.Options[defaultIndex].Value;
            }

            string value = rawValue.Trim();
            if (value.Length == 0)
            {
                return request.Options[defaultIndex].Value;
            }

            if (int.TryParse(value, out int selectedNumber) &&
                selectedNumber >= 1 &&
                selectedNumber <= request.Options.Count)
            {
                return request.Options[selectedNumber - 1].Value;
            }

            _console.MarkupLine(
                "[red]Enter a number from[/] " +
                Markup.Escape($"{1}") +
                " [red]to[/] " +
                Markup.Escape($"{request.Options.Count}") +
                "[red].[/]");
        }
    }

    private async Task<string> RequestTextFallbackAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        WriteTextPromptFallbackHeader(request);

        string? value = isSecret && !Console.IsInputRedirected
            ? ReadSecretLine(cancellationToken)
            : _fallbackInput.ReadLine();

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(value) && request.DefaultValue is not null)
        {
            return request.DefaultValue;
        }

        if (value is null)
        {
            throw new PromptCancelledException($"No value was provided for {request.Label}.");
        }

        return value;
    }

    private void WriteSelectionPromptFallback<T>(
        SelectionPromptRequest<T> request,
        int defaultIndex)
    {
        string title = request.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            _console.MarkupLine($"[bold]{Markup.Escape(title)}[/]");
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            if (request.DescriptionSupportsMarkup)
            {
                _console.MarkupLine(request.Description);
            }
            else
            {
                _console.MarkupLine(Markup.Escape(request.Description));
            }

            _console.WriteLine();
        }

        string? previousSection = null;
        for (int index = 0; index < request.Options.Count; index++)
        {
            SelectionPromptOption<T> option = request.Options[index];

            if (!string.IsNullOrWhiteSpace(option.Section) &&
                !string.Equals(option.Section, previousSection, StringComparison.Ordinal))
            {
                if (index > 0)
                {
                    _console.WriteLine();
                }

                _console.MarkupLine(
                    "[underline]" + Markup.Escape(option.Section.Trim()) + "[/]:");
                previousSection = option.Section;
            }

            string defaultSuffix = index == defaultIndex ? " (default)" : string.Empty;
            _console.MarkupLine(
                Markup.Escape($"{index + 1}. {option.Label}{defaultSuffix}"));

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                _console.MarkupLine(
                    "   " + Markup.Escape(option.Description));
            }
        }

        if (request.AutoSelectAfter is not null)
        {
            _console.WriteLine();
            _console.MarkupLine(
                "[dim]Default will be used after " +
                Markup.Escape($"{request.AutoSelectAfter.Value.TotalSeconds:0}") +
                "s.[/]");
        }

        _console.WriteLine();
    }

    private void WriteTextPromptFallbackHeader(TextPromptRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Label))
        {
            _console.MarkupLine($"[bold]{Markup.Escape(request.Label)}[/]");
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            _console.MarkupLine(Markup.Escape(request.Description));
        }

        _console.Markup("[bold]>[/] ");

        if (!string.IsNullOrEmpty(request.DefaultValue))
        {
            _console.Markup("[dim][" + Markup.Escape(request.DefaultValue) + "][/] ");
        }
    }

    private string ReadSecretLine(CancellationToken cancellationToken)
    {
        System.Text.StringBuilder builder = new();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n')
            {
                _console.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace || key.KeyChar is '\b' or '\u007f')
            {
                if (builder.Length > 0)
                {
                    builder.Remove(builder.Length - 1, 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
    }

    private async Task<string?> ReadLineWithTimeoutAsync(
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        Task<string?> readTask = Task.Run(_fallbackInput.ReadLine, cancellationToken);

        if (timeout is null)
        {
            return await readTask.WaitAsync(cancellationToken);
        }

        Task timeoutTask = Task.Delay(timeout.Value, cancellationToken);
        Task completedTask = await Task.WhenAny(readTask, timeoutTask);
        if (completedTask == readTask)
        {
            return await readTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private void WriteColoredStatus(string label, string message, string color)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _console.MarkupLine(
            $"[bold {color}]{Markup.Escape(label)}[/] " +
            Markup.Escape(message.Trim()));
    }

    private void WriteBlock(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _console.MarkupLine(Markup.Escape(message.Trim()));
        _console.WriteLine();
    }

    private bool TryConsumeProviderAuthKey(
        TextPromptRequest request,
        bool isSecret,
        out string providerAuthKey)
    {
        providerAuthKey = string.Empty;
        if (!isSecret || !IsProviderAuthKeyPrompt(request))
        {
            return false;
        }

        lock (_providerAuthKeySync)
        {
            if (_providerAuthKeyConsumed || string.IsNullOrWhiteSpace(_providerAuthKey))
            {
                return false;
            }

            providerAuthKey = _providerAuthKey;
            _providerAuthKeyConsumed = true;
            _providerAuthKey = null;
            return true;
        }
    }

    private static bool IsProviderAuthKeyPrompt(TextPromptRequest request)
    {
        string label = request.Label.Trim();
        return string.Equals(label, "API key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Provider auth key", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static IAnsiConsole CreateErrorConsole()
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error),
            ColorSystem = ColorSystemSupport.Detect,
            Ansi = AnsiSupport.Detect,
            Interactive = InteractionSupport.Detect,
        });
    }

    private static string? FindLabel<T>(
        IReadOnlyList<SelectionPromptOption<T>> options,
        T value)
    {
        foreach (SelectionPromptOption<T> option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                return option.Label;
            }
        }

        return value?.ToString();
    }
}
