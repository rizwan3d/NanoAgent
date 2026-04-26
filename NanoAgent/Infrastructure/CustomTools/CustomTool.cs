using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Infrastructure.CustomTools;

internal sealed class CustomTool : ITool
{
    private const int MaxRenderTextLength = 4000;

    private readonly CustomToolConfiguration _configuration;
    private readonly IProcessRunner _processRunner;
    private readonly string _permissionRequirements;

    public CustomTool(
        string toolName,
        CustomToolConfiguration configuration,
        IProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(processRunner);

        Name = toolName.Trim();
        _configuration = configuration;
        _processRunner = processRunner;
        Description = configuration.GetDescription();
        Schema = configuration.GetSchema();
        _permissionRequirements = CustomToolJson.CreatePermissionRequirements(
            configuration.Name,
            configuration.GetApprovalMode());
    }

    public string Description { get; }

    public string Name { get; }

    public string PermissionRequirements => _permissionRequirements;

    public string Schema { get; }

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_configuration.Command))
        {
            return ToolResultFactory.ExecutionError(
                "custom_tool_command_missing",
                $"Custom tool '{_configuration.Name}' does not configure a command.",
                new ToolRenderPayload(
                    $"Custom tool failed: {_configuration.Name}",
                    "The tool configuration is missing 'command'."));
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_configuration.TimeoutSeconds));

        ProcessExecutionResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(
                new ProcessExecutionRequest(
                    _configuration.Command!,
                    _configuration.Args,
                    StandardInput: CustomToolJson.CreateToolInput(context, _configuration.Name),
                    WorkingDirectory: GetWorkingDirectory(context),
                    MaxOutputCharacters: _configuration.MaxOutputChars,
                    EnvironmentVariables: CreateEnvironment(context)),
                timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResultFactory.ExecutionError(
                "custom_tool_timeout",
                $"Custom tool '{_configuration.Name}' timed out after {_configuration.TimeoutSeconds} seconds.",
                new ToolRenderPayload(
                    $"Custom tool timed out: {_configuration.Name}",
                    $"The process did not finish within {_configuration.TimeoutSeconds} seconds."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ToolResultFactory.ExecutionError(
                "custom_tool_failed",
                $"Custom tool '{_configuration.Name}' failed to start: {exception.Message}",
                new ToolRenderPayload(
                    $"Custom tool failed: {_configuration.Name}",
                    exception.Message));
        }

        if (processResult.ExitCode != 0)
        {
            string failureText = BuildProcessPreview(processResult);
            return ToolResult.ExecutionError(
                $"Custom tool '{_configuration.Name}' exited with code {processResult.ExitCode}.",
                CustomToolJson.CreateProcessFailurePayload(
                    _configuration.Name,
                    processResult.ExitCode,
                    processResult.StandardOutput,
                    processResult.StandardError).GetRawText(),
                new ToolRenderPayload(
                    $"Custom tool failed: {_configuration.Name}",
                    failureText));
        }

        return CreateSuccessOrProtocolResult(processResult.StandardOutput);
    }

    private ToolResult CreateSuccessOrProtocolResult(string standardOutput)
    {
        string trimmedOutput = standardOutput.Trim();
        if (trimmedOutput.Length == 0)
        {
            JsonElement emptyPayload = CustomToolJson.CreateTextPayload(_configuration.Name, string.Empty);
            return ToolResultFactory.Success(
                $"Custom tool '{_configuration.Name}' completed with no output.",
                emptyPayload,
                ToolJsonContext.Default.JsonElement,
                new ToolRenderPayload(
                    $"Custom tool: {_configuration.Name}",
                    "Completed with no output."));
        }

        if (!TryParseJsonObject(trimmedOutput, out JsonElement root))
        {
            JsonElement textPayload = CustomToolJson.CreateTextPayload(_configuration.Name, trimmedOutput);
            return ToolResultFactory.Success(
                $"Custom tool '{_configuration.Name}' completed.",
                textPayload,
                ToolJsonContext.Default.JsonElement,
                new ToolRenderPayload(
                    $"Custom tool: {_configuration.Name}",
                    Truncate(trimmedOutput, MaxRenderTextLength)));
        }

        if (!TryParseProtocolResult(root, out CustomToolProtocolResult protocol))
        {
            return ToolResultFactory.Success(
                $"Custom tool '{_configuration.Name}' completed.",
                root,
                ToolJsonContext.Default.JsonElement,
                new ToolRenderPayload(
                    $"Custom tool: {_configuration.Name}",
                    Truncate(trimmedOutput, MaxRenderTextLength)));
        }

        JsonElement payload = CustomToolJson.CreateProtocolPayload(
            _configuration.Name,
            protocol.Result ?? root);
        ToolRenderPayload renderPayload = new(
            string.IsNullOrWhiteSpace(protocol.RenderTitle)
                ? $"Custom tool: {_configuration.Name}"
                : protocol.RenderTitle!,
            Truncate(protocol.RenderText ?? protocol.Message ?? trimmedOutput, MaxRenderTextLength));

        if (protocol.IsInvalidArguments)
        {
            return ToolResult.InvalidArguments(
                protocol.Message ?? $"Custom tool '{_configuration.Name}' reported invalid arguments.",
                payload.GetRawText(),
                renderPayload);
        }

        if (protocol.IsError)
        {
            return ToolResult.ExecutionError(
                protocol.Message ?? $"Custom tool '{_configuration.Name}' reported an error.",
                payload.GetRawText(),
                renderPayload);
        }

        return ToolResultFactory.Success(
            protocol.Message ?? $"Custom tool '{_configuration.Name}' completed.",
            payload,
            ToolJsonContext.Default.JsonElement,
            renderPayload);
    }

    private string GetWorkingDirectory(ToolExecutionContext context)
    {
        return string.IsNullOrWhiteSpace(_configuration.Cwd)
            ? context.Session.WorkspacePath
            : _configuration.Cwd!;
    }

    private IReadOnlyDictionary<string, string> CreateEnvironment(ToolExecutionContext context)
    {
        Dictionary<string, string> environment = new(_configuration.Env, StringComparer.Ordinal)
        {
            ["NANOAGENT_TOOL_NAME"] = Name,
            ["NANOAGENT_CUSTOM_TOOL_NAME"] = _configuration.Name,
            ["NANOAGENT_SESSION_ID"] = context.Session.SessionId,
            ["NANOAGENT_WORKSPACE_PATH"] = context.Session.WorkspacePath,
            ["NANOAGENT_WORKING_DIRECTORY"] = context.Session.WorkingDirectory
        };

        return environment;
    }

    private static bool TryParseJsonObject(
        string value,
        out JsonElement root)
    {
        root = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseProtocolResult(
        JsonElement root,
        out CustomToolProtocolResult result)
    {
        result = default;

        string? status = GetOptionalString(root, "status");
        bool hasProtocolField =
            !string.IsNullOrWhiteSpace(status) ||
            root.TryGetProperty("isError", out _) ||
            root.TryGetProperty("message", out _) ||
            root.TryGetProperty("data", out _) ||
            root.TryGetProperty("result", out _) ||
            root.TryGetProperty("renderText", out _);

        if (!hasProtocolField)
        {
            return false;
        }

        JsonElement? payload = null;
        if (root.TryGetProperty("data", out JsonElement data))
        {
            payload = data.Clone();
        }
        else if (root.TryGetProperty("result", out JsonElement resultElement))
        {
            payload = resultElement.Clone();
        }

        bool isError = root.TryGetProperty("isError", out JsonElement isErrorElement) &&
                       isErrorElement.ValueKind == JsonValueKind.True;
        bool invalidArguments =
            string.Equals(status, "invalid_arguments", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "invalidArguments", StringComparison.OrdinalIgnoreCase);
        bool statusError =
            string.Equals(status, "error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "failure", StringComparison.OrdinalIgnoreCase);

        result = new CustomToolProtocolResult(
            statusError || isError,
            invalidArguments,
            GetOptionalString(root, "message"),
            payload,
            GetOptionalString(root, "renderTitle"),
            GetOptionalString(root, "renderText") ??
            GetOptionalString(root, "displayText"));
        return true;
    }

    private static string? GetOptionalString(
        JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string BuildProcessPreview(ProcessExecutionResult processResult)
    {
        string output = !string.IsNullOrWhiteSpace(processResult.StandardError)
            ? processResult.StandardError
            : processResult.StandardOutput;

        if (string.IsNullOrWhiteSpace(output))
        {
            return $"Exit code: {processResult.ExitCode}. No output was captured.";
        }

        return $"Exit code: {processResult.ExitCode}.{Environment.NewLine}{Truncate(output.Trim(), MaxRenderTextLength)}";
    }

    private static string Truncate(
        string value,
        int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength <= 3
            ? value[..maxLength]
            : value[..(maxLength - 3)] + "...";
    }

    private readonly record struct CustomToolProtocolResult(
        bool IsError,
        bool IsInvalidArguments,
        string? Message,
        JsonElement? Result,
        string? RenderTitle,
        string? RenderText);
}
