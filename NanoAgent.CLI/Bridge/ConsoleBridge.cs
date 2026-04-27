using System.Text;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;
using NanoAgent.Application.UI;

namespace NanoAgent.CLI;

public sealed class ConsoleBridge : IUiBridge
{
    private readonly TextReader _input;
    private readonly TextWriter _error;
    private readonly IPlanOutputFormatter _planOutputFormatter;
    private readonly IToolOutputFormatter _toolOutputFormatter;
    private readonly object _providerAuthKeySync = new();
    private string? _providerAuthKey;
    private bool _providerAuthKeyConsumed;

    public ConsoleBridge(string? providerAuthKey = null)
        : this(
            Console.In,
            Console.Error,
            new ToolOutputFormatter(),
            new PlanOutputFormatter(),
            providerAuthKey)
    {
    }

    internal ConsoleBridge(
        TextReader input,
        TextWriter error,
        IToolOutputFormatter toolOutputFormatter,
        IPlanOutputFormatter planOutputFormatter,
        string? providerAuthKey = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _error = error ?? throw new ArgumentNullException(nameof(error));
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

        int defaultIndex = Math.Clamp(request.DefaultIndex, 0, request.Options.Count - 1);
        if (Console.IsInputRedirected)
        {
            throw new PromptCancelledException(
                $"Prompt '{request.Title}' requires interactive input.");
        }

        WriteSelectionPrompt(request, defaultIndex);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _error.Write($"Select [{defaultIndex + 1}]: ");

            string? rawValue = await ReadLineWithTimeoutAsync(
                request.AutoSelectAfter,
                cancellationToken);

            if (rawValue is null)
            {
                _error.WriteLine();
                _error.WriteLine($"Using default: {request.Options[defaultIndex].Label}");
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

            _error.WriteLine($"Enter a number from 1 to {request.Options.Count}.");
        }
    }

    public Task<string> RequestTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryConsumeProviderAuthKey(request, isSecret, out string providerAuthKey))
        {
            return Task.FromResult(providerAuthKey);
        }

        WriteTextPromptHeader(request);

        string? value = isSecret && !Console.IsInputRedirected
            ? ReadSecretLine(cancellationToken)
            : _input.ReadLine();

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(value) &&
            request.DefaultValue is not null)
        {
            return Task.FromResult(request.DefaultValue);
        }

        if (value is null)
        {
            throw new PromptCancelledException($"No value was provided for {request.Label}.");
        }

        return Task.FromResult(value);
    }

    public void ShowError(string message)
    {
        WriteStatus("Error", message);
    }

    public void ShowInfo(string message)
    {
        WriteStatus("Info", message);
    }

    public void ShowSuccess(string message)
    {
        WriteStatus("Success", message);
    }

    public void ShowToolCalls(IReadOnlyList<ConversationToolCall> toolCalls)
    {
        string[] descriptions = toolCalls
            .Select(_toolOutputFormatter.DescribeCall)
            .Where(static description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        if (descriptions.Length == 0)
        {
            WriteStatus("Tools", "Running tools.");
            return;
        }

        WriteStatus("Tools", "Running:");
        foreach (string description in descriptions)
        {
            _error.WriteLine($"  - {description}");
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

    private void WriteSelectionPrompt<T>(
        SelectionPromptRequest<T> request,
        int defaultIndex)
    {
        WriteBlock(request.Title);

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            _error.WriteLine(request.Description);
            _error.WriteLine();
        }

        for (int index = 0; index < request.Options.Count; index++)
        {
            SelectionPromptOption<T> option = request.Options[index];
            string defaultSuffix = index == defaultIndex ? " (default)" : string.Empty;
            _error.WriteLine($"{index + 1}. {option.Label}{defaultSuffix}");

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                _error.WriteLine($"   {option.Description}");
            }
        }

        if (request.AutoSelectAfter is not null)
        {
            _error.WriteLine();
            _error.WriteLine($"Default will be used after {request.AutoSelectAfter.Value.TotalSeconds:0}s.");
        }

        _error.WriteLine();
    }

    private void WriteTextPromptHeader(TextPromptRequest request)
    {
        WriteBlock(request.Label);

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            _error.WriteLine(request.Description);
        }

        _error.Write("> ");

        if (!string.IsNullOrEmpty(request.DefaultValue))
        {
            _error.Write($"[{request.DefaultValue}] ");
        }
    }

    private string ReadSecretLine(CancellationToken cancellationToken)
    {
        StringBuilder builder = new();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n')
            {
                _error.WriteLine();
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
        Task<string?> readTask = Task.Run(_input.ReadLine);

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

    private void WriteStatus(string label, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _error.WriteLine($"[{label}] {message.Trim()}");
    }

    private void WriteBlock(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _error.WriteLine(message.Trim());
        _error.WriteLine();
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
}
