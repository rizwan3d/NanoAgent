using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Infrastructure.Secrets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NanoAgent.Infrastructure.Hooks;

internal sealed class ShellLifecycleHookService : ILifecycleHookService
{
    private const string EventWildcard = "*";

    private readonly LifecycleHookSettings _settings;
    private readonly ILogger<ShellLifecycleHookService> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public ShellLifecycleHookService(
        IOptions<ApplicationOptions> options,
        IProcessRunner processRunner,
        IWorkspaceRootProvider workspaceRootProvider,
        ILogger<ShellLifecycleHookService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Value.Hooks ?? new LifecycleHookSettings();
        _processRunner = processRunner;
        _workspaceRootProvider = workspaceRootProvider;
        _logger = logger;
    }

    public async Task<LifecycleHookRunResult> RunAsync(
        LifecycleHookContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        LifecycleHookRule[] rules = _settings.Rules ?? [];
        if (!_settings.Enabled ||
            rules.Length == 0)
        {
            return LifecycleHookRunResult.Allowed();
        }

        string eventName = LifecycleHookEvents.Normalize(context.EventName);
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return LifecycleHookRunResult.Allowed();
        }

        LifecycleHookContext normalizedContext = NormalizeContext(context, eventName);
        foreach (LifecycleHookRule rule in rules.Where(rule => ShouldRun(rule, normalizedContext)))
        {
            LifecycleHookRunResult result = await RunRuleAsync(
                rule,
                normalizedContext,
                cancellationToken);
            if (!result.IsAllowed)
            {
                return result;
            }
        }

        return LifecycleHookRunResult.Allowed();
    }

    private async Task<LifecycleHookRunResult> RunRuleAsync(
        LifecycleHookRule rule,
        LifecycleHookContext context,
        CancellationToken cancellationToken)
    {
        string hookName = string.IsNullOrWhiteSpace(rule.Name)
            ? rule.Command!.Trim()
            : rule.Name.Trim();
        bool continueOnError = rule.ContinueOnError ??
                               !LifecycleHookEvents.IsBeforeEvent(context.EventName);

        try
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(GetTimeoutSeconds(rule)));

            ProcessExecutionResult result = await _processRunner.RunAsync(
                CreateProcessRequest(rule, context),
                timeoutSource.Token);

            if (result.ExitCode == 0)
            {
                return LifecycleHookRunResult.Allowed();
            }

            string message = CreateFailureMessage(hookName, context.EventName, result);
            _logger.LogWarning("{Message}", message);
            return continueOnError
                ? LifecycleHookRunResult.Allowed()
                : LifecycleHookRunResult.Blocked(hookName, message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            string message = $"Lifecycle hook '{hookName}' timed out while handling '{context.EventName}'.";
            _logger.LogWarning("{Message}", message);
            return continueOnError
                ? LifecycleHookRunResult.Allowed()
                : LifecycleHookRunResult.Blocked(hookName, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = $"Lifecycle hook '{hookName}' failed while handling '{context.EventName}': {exception.Message}";
            _logger.LogWarning(exception, "{Message}", message);
            return continueOnError
                ? LifecycleHookRunResult.Allowed()
                : LifecycleHookRunResult.Blocked(hookName, message);
        }
    }

    private ProcessExecutionRequest CreateProcessRequest(
        LifecycleHookRule rule,
        LifecycleHookContext context)
    {
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        string workingDirectory = ResolveWorkingDirectory(workspaceRoot, rule.WorkingDirectory);
        string payloadJson = JsonSerializer.Serialize(
            context,
            LifecycleHookJsonContext.Default.LifecycleHookContext);

        (string fileName, IReadOnlyList<string> arguments) = rule.RunInShell
            ? CreateShellCommand(rule.Command!, rule.Arguments)
            : (rule.Command!.Trim(), NormalizeArguments(rule.Arguments));

        return new ProcessExecutionRequest(
            fileName,
            arguments,
            StandardInput: payloadJson,
            WorkingDirectory: workingDirectory,
            MaxOutputCharacters: GetMaxOutputCharacters(rule),
            EnvironmentVariables: CreateEnvironment(context, workspaceRoot));
    }

    private static (string FileName, IReadOnlyList<string> Arguments) CreateShellCommand(
        string command,
        IReadOnlyList<string> arguments)
    {
        string commandLine = AppendShellArguments(command.Trim(), arguments);
        if (OperatingSystem.IsWindows())
        {
            return ("powershell", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", commandLine]);
        }

        return ("/bin/sh", ["-c", commandLine]);
    }

    private static string AppendShellArguments(
        string command,
        IReadOnlyList<string> arguments)
    {
        string[] normalizedArguments = NormalizeArguments(arguments);
        if (normalizedArguments.Length == 0)
        {
            return command;
        }

        return command + " " + string.Join(" ", normalizedArguments.Select(QuoteShellArgument));
    }

    private static string QuoteShellArgument(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string[] NormalizeArguments(IReadOnlyList<string>? arguments)
    {
        return (arguments ?? [])
            .Where(static argument => !string.IsNullOrWhiteSpace(argument))
            .Select(static argument => argument.Trim())
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> CreateEnvironment(
        LifecycleHookContext context,
        string workspaceRoot)
    {
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NANOAGENT_HOOK_EVENT"] = context.EventName,
            ["NANOAGENT_WORKSPACE_ROOT"] = workspaceRoot
        };

        AddOptional(environment, "NANOAGENT_APPLICATION", context.ApplicationName);
        AddOptional(environment, "NANOAGENT_SESSION_ID", context.SessionId);
        AddOptional(environment, "NANOAGENT_TOOL_CALL_ID", context.ToolCallId);
        AddOptional(environment, "NANOAGENT_TOOL_NAME", context.ToolName);
        AddOptional(environment, "NANOAGENT_PATH", context.Path);
        AddOptional(environment, "NANOAGENT_SHELL_COMMAND", context.ShellCommand);
        AddOptional(environment, "NANOAGENT_RESULT_STATUS", context.ResultStatus);

        if (context.ShellExitCode is not null)
        {
            environment["NANOAGENT_SHELL_EXIT_CODE"] = context.ShellExitCode.Value.ToString();
        }

        return environment;
    }

    private static void AddOptional(
        IDictionary<string, string> environment,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            environment[key] = value.Trim();
        }
    }

    private static string ResolveWorkingDirectory(
        string workspaceRoot,
        string? configuredWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredWorkingDirectory))
        {
            return workspaceRoot;
        }

        return Path.IsPathRooted(configuredWorkingDirectory)
            ? Path.GetFullPath(configuredWorkingDirectory)
            : WorkspacePath.Resolve(workspaceRoot, configuredWorkingDirectory.Trim());
    }

    private static LifecycleHookContext NormalizeContext(
        LifecycleHookContext context,
        string eventName)
    {
        context.EventName = eventName;
        return context;
    }

    private bool ShouldRun(
        LifecycleHookRule rule,
        LifecycleHookContext context)
    {
        return rule.Enabled &&
               !string.IsNullOrWhiteSpace(rule.Command) &&
               MatchesEvent(rule, context.EventName) &&
               MatchesAnyOptional(rule.ToolNames, context.ToolName) &&
               MatchesAnyOptional(rule.PathPatterns, context.Path) &&
               MatchesAnyOptional(rule.ShellCommandPatterns, context.ShellCommand);
    }

    private static bool MatchesEvent(
        LifecycleHookRule rule,
        string eventName)
    {
        string[] events = (rule.Events ?? [])
            .Append(rule.Event)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => LifecycleHookEvents.Normalize(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return events.Length > 0 &&
               events.Any(pattern => string.Equals(pattern, EventWildcard, StringComparison.Ordinal) ||
                                     MatchesPattern(eventName, pattern));
    }

    private static bool MatchesAnyOptional(
        IReadOnlyList<string>? patterns,
        string? value)
    {
        string[] normalizedPatterns = (patterns ?? [])
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(static pattern => pattern.Trim())
            .ToArray();

        if (normalizedPatterns.Length == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(value) &&
               normalizedPatterns.Any(pattern => MatchesPattern(value!, pattern));
    }

    private static bool MatchesPattern(
        string value,
        string pattern)
    {
        string normalizedValue = value.Trim().Replace('\\', '/');
        string normalizedPattern = pattern.Trim().Replace('\\', '/');
        string regexPattern = "^" +
                              Regex.Escape(normalizedPattern)
                                  .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                                  .Replace("\\*", ".*", StringComparison.Ordinal)
                                  .Replace("\\?", ".", StringComparison.Ordinal) +
                              "$";

        return Regex.IsMatch(
            normalizedValue,
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private int GetTimeoutSeconds(LifecycleHookRule rule)
    {
        return Math.Max(1, rule.TimeoutSeconds ?? _settings.DefaultTimeoutSeconds);
    }

    private int GetMaxOutputCharacters(LifecycleHookRule rule)
    {
        return Math.Max(0, rule.MaxOutputCharacters ?? _settings.MaxOutputCharacters);
    }

    private static string CreateFailureMessage(
        string hookName,
        string eventName,
        ProcessExecutionResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        detail = string.IsNullOrWhiteSpace(detail)
            ? "No output."
            : detail.Trim();

        return $"Lifecycle hook '{hookName}' exited with code {result.ExitCode} while handling '{eventName}': {detail}";
    }
}
