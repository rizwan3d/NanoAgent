using System.Globalization;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools.Services;

internal sealed class RegistryBackedToolInvoker : IToolInvoker
{
    private static readonly TimeSpan AgentDelegateTimeout = TimeSpan.FromMinutes(10);

    private readonly TimeSpan _defaultTimeout;
    private readonly ILifecycleHookService _lifecycleHookService;
    private readonly IPermissionApprovalPrompt _permissionApprovalPrompt;
    private readonly IPermissionEvaluator _permissionEvaluator;
    private readonly SemaphoreSlim _permissionApprovalSemaphore = new(1, 1);
    private readonly IToolRegistry _toolRegistry;

    public RegistryBackedToolInvoker(
        IToolRegistry toolRegistry,
        IPermissionEvaluator permissionEvaluator,
        IPermissionApprovalPrompt permissionApprovalPrompt,
        TimeSpan? defaultTimeout = null,
        ILifecycleHookService? lifecycleHookService = null)
    {
        _toolRegistry = toolRegistry;
        _permissionEvaluator = permissionEvaluator;
        _permissionApprovalPrompt = permissionApprovalPrompt;
        _lifecycleHookService = lifecycleHookService ?? DisabledLifecycleHookService.Instance;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<ToolInvocationResult> InvokeAsync(
        ConversationToolCall toolCall,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        IReadOnlySet<string> allowedToolNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(allowedToolNames);
        cancellationToken.ThrowIfCancellationRequested();

        if (!allowedToolNames.Contains(toolCall.Name))
        {
            string phaseName = executionPhase == ConversationExecutionPhase.Planning
                ? "planning"
                : "execution";

            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.Name,
                ToolResultFactory.PermissionDenied(
                    "tool_not_available_in_phase",
                    $"Tool '{toolCall.Name}' is not available during the {phaseName} phase.",
                    new ToolRenderPayload(
                        $"Tool unavailable: {toolCall.Name}",
                        $"'{toolCall.Name}' cannot be used during the {phaseName} phase.")));
        }

        if (!_toolRegistry.TryResolve(toolCall.Name, out ToolRegistration? registration) || registration is null)
        {
            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.Name,
                ToolResultFactory.NotFound(
                    "tool_not_found",
                    $"Tool '{toolCall.Name}' is not registered in this agent.",
                    new ToolRenderPayload(
                        $"Unknown tool: {toolCall.Name}",
                        $"The LLM requested '{toolCall.Name}', but this agent does not have that tool registered.")));
        }

        JsonElement arguments;

        try
        {
            arguments = ParseArguments(toolCall);
        }
        catch (JsonException exception)
        {
            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.Name,
                ToolResultFactory.InvalidArguments(
                    "invalid_json_arguments",
                    $"Tool '{toolCall.Name}' received invalid JSON arguments: {exception.Message}",
                    new ToolRenderPayload(
                        $"Invalid tool arguments: {toolCall.Name}",
                        $"The LLM produced malformed JSON arguments for '{toolCall.Name}'.")));
        }
        catch (InvalidOperationException exception)
        {
            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.Name,
                ToolResultFactory.InvalidArguments(
                    "invalid_tool_arguments",
                    exception.Message,
                    new ToolRenderPayload(
                        $"Invalid tool arguments: {toolCall.Name}",
                        exception.Message)));
        }

        ToolExecutionContext executionContext = new(
            toolCall.Id,
            toolCall.Name,
            arguments,
            session,
            executionPhase);

        PermissionEvaluationResult permissionResult = _permissionEvaluator.Evaluate(
            registration.PermissionPolicy,
            new PermissionEvaluationContext(executionContext));

        if (permissionResult.Decision == PermissionEvaluationDecision.RequiresApproval)
        {
            await _permissionApprovalSemaphore.WaitAsync(cancellationToken);
            try
            {
                permissionResult = await ResolveApprovalAsync(
                    registration.PermissionPolicy,
                    executionContext,
                    permissionResult,
                    cancellationToken);
            }
            finally
            {
                _permissionApprovalSemaphore.Release();
            }
        }

        if (!permissionResult.IsAllowed)
        {
            await RunHooksAsync(
                [LifecycleHookEvents.OnPermissionDenied],
                executionContext,
                result: null,
                cancellationToken);

            string reasonCode = permissionResult.ReasonCode!;
            string reason = permissionResult.Reason!;
            string title = permissionResult.Decision == PermissionEvaluationDecision.RequiresApproval
                ? $"Approval required: {toolCall.Name}"
                : $"Permission denied: {toolCall.Name}";

            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.Name,
                ToolResultFactory.PermissionDenied(
                    reasonCode,
                    reason,
                    new ToolRenderPayload(
                        title,
                        reason)));
        }

        LifecycleHookRunResult beforeHookResult = await RunHooksAsync(
            CreateBeforeHookEvents(executionContext),
            executionContext,
            result: null,
            cancellationToken);
        if (!beforeHookResult.IsAllowed)
        {
            return CreateHookBlockedInvocationResult(toolCall, beforeHookResult);
        }

        TimeSpan timeout = GetToolTimeout(toolCall.Name);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        ToolResult toolResult;
        try
        {
            toolResult = await registration.Tool.ExecuteAsync(
                executionContext,
                timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            toolResult = ToolResultFactory.ExecutionError(
                "tool_timeout",
                $"Tool '{toolCall.Name}' timed out after {timeout.TotalSeconds:0} seconds.",
                new ToolRenderPayload(
                    $"Tool timed out: {toolCall.Name}",
                    $"'{toolCall.Name}' did not finish within {timeout.TotalSeconds:0} seconds."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            toolResult = ToolResultFactory.ExecutionError(
                "tool_execution_failed",
                $"Tool execution failed unexpectedly: {exception.Message}",
                new ToolRenderPayload(
                    $"Tool failed: {toolCall.Name}",
                    exception.Message));
        }

        LifecycleHookRunResult afterHookResult = await RunHooksAsync(
            CreateAfterHookEvents(executionContext, toolResult),
            executionContext,
            toolResult,
            cancellationToken);

        return !afterHookResult.IsAllowed
            ? CreateHookBlockedInvocationResult(toolCall, afterHookResult)
            : new ToolInvocationResult(toolCall.Id, toolCall.Name, toolResult);
    }

    private TimeSpan GetToolTimeout(string toolName)
    {
        if (toolName.StartsWith(AgentToolNames.McpToolPrefix, StringComparison.Ordinal) ||
            toolName.StartsWith(AgentToolNames.CustomToolPrefix, StringComparison.Ordinal))
        {
            return TimeSpan.FromMinutes(10);
        }

        return toolName is AgentToolNames.AgentDelegate or AgentToolNames.AgentOrchestrate
            ? AgentDelegateTimeout
            : _defaultTimeout;
    }

    private async Task<LifecycleHookRunResult> RunHooksAsync(
        IReadOnlyList<string> eventNames,
        ToolExecutionContext executionContext,
        ToolResult? result,
        CancellationToken cancellationToken)
    {
        foreach (string eventName in eventNames)
        {
            LifecycleHookRunResult hookResult = await _lifecycleHookService.RunAsync(
                CreateHookContext(eventName, executionContext, result),
                cancellationToken);
            if (!hookResult.IsAllowed)
            {
                return hookResult;
            }
        }

        return LifecycleHookRunResult.Allowed();
    }

    private static IReadOnlyList<string> CreateBeforeHookEvents(ToolExecutionContext context)
    {
        List<string> events = [LifecycleHookEvents.BeforeToolCall];
        AddSpecificHookEvents(context, result: null, events, before: true);
        return events.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> CreateAfterHookEvents(
        ToolExecutionContext context,
        ToolResult result)
    {
        List<string> events = [];
        AddSpecificHookEvents(context, result, events, before: false);
        events.Add(LifecycleHookEvents.AfterToolCall);

        if (!result.IsSuccess)
        {
            events.Add(LifecycleHookEvents.AfterToolFailure);
        }

        return events.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddSpecificHookEvents(
        ToolExecutionContext context,
        ToolResult? result,
        List<string> events,
        bool before)
    {
        switch (context.ToolName)
        {
            case AgentToolNames.FileRead:
            case AgentToolNames.DirectoryList:
                events.Add(before ? LifecycleHookEvents.BeforeFileRead : LifecycleHookEvents.AfterFileRead);
                break;

            case AgentToolNames.FileWrite:
            case AgentToolNames.ApplyPatch:
                events.Add(before ? LifecycleHookEvents.BeforeFileWrite : LifecycleHookEvents.AfterFileWrite);
                break;

            case AgentToolNames.FileDelete:
                events.Add(before ? LifecycleHookEvents.BeforeFileDelete : LifecycleHookEvents.AfterFileDelete);
                break;

            case AgentToolNames.SearchFiles:
            case AgentToolNames.TextSearch:
                events.Add(before ? LifecycleHookEvents.BeforeFileSearch : LifecycleHookEvents.AfterFileSearch);
                break;

            case AgentToolNames.ShellCommand:
                events.Add(before ? LifecycleHookEvents.BeforeShellCommand : LifecycleHookEvents.AfterShellCommand);
                if (!before &&
                    TryGetShellExitCode(result, out int shellExitCode) &&
                    shellExitCode != 0)
                {
                    events.Add(LifecycleHookEvents.AfterShellFailure);
                }

                break;

            case AgentToolNames.WebRun:
            case AgentToolNames.HeadlessBrowser:
                events.Add(before ? LifecycleHookEvents.BeforeWebRequest : LifecycleHookEvents.AfterWebRequest);
                break;

            case AgentToolNames.LessonMemory:
                AddMemoryHookEvents(context, events, before);
                break;

            case AgentToolNames.AgentDelegate:
            case AgentToolNames.AgentOrchestrate:
                events.Add(before ? LifecycleHookEvents.BeforeAgentDelegate : LifecycleHookEvents.AfterAgentDelegate);
                break;
        }
    }

    private static void AddMemoryHookEvents(
        ToolExecutionContext context,
        List<string> events,
        bool before)
    {
        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "action", out string? action))
        {
            return;
        }

        if (string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
        {
            events.Add(before ? LifecycleHookEvents.BeforeMemorySave : LifecycleHookEvents.AfterMemorySave);
            events.Add(before ? LifecycleHookEvents.BeforeMemoryWrite : LifecycleHookEvents.AfterMemoryWrite);
            return;
        }

        if (string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            events.Add(before ? LifecycleHookEvents.BeforeMemoryWrite : LifecycleHookEvents.AfterMemoryWrite);
        }
    }

    private static LifecycleHookContext CreateHookContext(
        string eventName,
        ToolExecutionContext executionContext,
        ToolResult? result)
    {
        LifecycleHookContext context = new()
        {
            ApplicationName = executionContext.Session.ApplicationName,
            ArgumentsJson = executionContext.Arguments.GetRawText(),
            EventName = eventName,
            ExecutionPhase = executionContext.ExecutionPhase.ToString(),
            ModelId = executionContext.Session.ActiveModelId,
            ProviderName = executionContext.Session.ProviderName,
            ResultMessage = result?.Message,
            ResultStatus = result?.Status.ToString(),
            ResultSuccess = result?.IsSuccess,
            SessionId = executionContext.Session.SessionId,
            ToolCallId = executionContext.ToolCallId,
            ToolName = executionContext.ToolName
        };

        AddToolSpecificHookContext(executionContext, result, context);
        return context;
    }

    private static void AddToolSpecificHookContext(
        ToolExecutionContext executionContext,
        ToolResult? result,
        LifecycleHookContext context)
    {
        if (TryGetRequestedPath(executionContext, out string? path))
        {
            context.Path = path;
        }

        if (string.Equals(executionContext.ToolName, AgentToolNames.ShellCommand, StringComparison.Ordinal))
        {
            if (ToolArguments.TryGetNonEmptyString(executionContext.Arguments, "command", out string? command))
            {
                context.ShellCommand = command;
            }

            if (ToolArguments.TryGetNonEmptyString(executionContext.Arguments, "workingDirectory", out string? workingDirectory))
            {
                context.Metadata["workingDirectory"] = workingDirectory!;
            }

            if (TryGetShellExitCode(result, out int exitCode))
            {
                context.ShellExitCode = exitCode;
            }

            if (TryGetJsonString(result?.JsonResult, "Command", out string? executedCommand))
            {
                context.ShellCommand = executedCommand;
            }
        }

        if (string.Equals(executionContext.ToolName, AgentToolNames.LessonMemory, StringComparison.Ordinal))
        {
            context.MemoryAction = ToolArguments.GetOptionalString(executionContext.Arguments, "action");
            context.MemoryTrigger = ToolArguments.GetOptionalString(executionContext.Arguments, "trigger");
            context.MemoryProblem = ToolArguments.GetOptionalString(executionContext.Arguments, "problem");
        }

        if (string.Equals(executionContext.ToolName, AgentToolNames.AgentDelegate, StringComparison.Ordinal) &&
            ToolArguments.TryGetNonEmptyString(executionContext.Arguments, "task", out string? delegatedTask))
        {
            context.Metadata["delegatedTask"] = delegatedTask!;
        }

        if (string.Equals(executionContext.ToolName, AgentToolNames.AgentOrchestrate, StringComparison.Ordinal) &&
            executionContext.Arguments.TryGetProperty("tasks", out JsonElement tasksElement) &&
            tasksElement.ValueKind == JsonValueKind.Array)
        {
            context.Metadata["delegatedTaskCount"] = tasksElement.GetArrayLength().ToString(CultureInfo.InvariantCulture);
        }
    }

    private static bool TryGetRequestedPath(
        ToolExecutionContext executionContext,
        out string? path)
    {
        path = null;
        string? requestedPath = executionContext.ToolName switch
        {
            AgentToolNames.FileRead or
            AgentToolNames.FileWrite or
            AgentToolNames.FileDelete or
            AgentToolNames.DirectoryList or
            AgentToolNames.SearchFiles or
            AgentToolNames.TextSearch or
            AgentToolNames.ShellCommand => ToolArguments.GetOptionalString(executionContext.Arguments, "path") ??
                                          ToolArguments.GetOptionalString(executionContext.Arguments, "workingDirectory"),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return false;
        }

        try
        {
            path = executionContext.Session.ResolvePathFromWorkingDirectory(requestedPath);
            return true;
        }
        catch (InvalidOperationException)
        {
            path = requestedPath.Trim();
            return true;
        }
    }

    private static bool TryGetShellExitCode(
        ToolResult? result,
        out int exitCode)
    {
        exitCode = 0;
        return TryGetJsonInt(result?.JsonResult, "ExitCode", out exitCode) ||
               TryGetJsonInt(result?.JsonResult, "exitCode", out exitCode);
    }

    private static bool TryGetJsonInt(
        string? json,
        string propertyName,
        out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out JsonElement property) &&
                   property.TryGetInt32(out value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetJsonString(
        string? json,
        string propertyName,
        out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.GetString()))
            {
                return false;
            }

            value = property.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ToolInvocationResult CreateHookBlockedInvocationResult(
        ConversationToolCall toolCall,
        LifecycleHookRunResult hookResult)
    {
        string message = hookResult.Message ??
                         $"Lifecycle hook '{hookResult.FailedHookName}' blocked tool '{toolCall.Name}'.";
        return new ToolInvocationResult(
            toolCall.Id,
            toolCall.Name,
            ToolResultFactory.ExecutionError(
                "lifecycle_hook_blocked",
                message,
                new ToolRenderPayload(
                    $"Lifecycle hook blocked: {toolCall.Name}",
                    message)));
    }

    private async Task<PermissionEvaluationResult> ResolveApprovalAsync(
        ToolPermissionPolicy permissionPolicy,
        ToolExecutionContext executionContext,
        PermissionEvaluationResult permissionResult,
        CancellationToken cancellationToken)
    {
        PermissionRequestDescriptor request = permissionResult.Request ??
                                              new PermissionRequestDescriptor(
                                                  executionContext.ToolName,
                                                  executionContext.ToolName,
                                                  [executionContext.ToolName],
                                                  []);

        PermissionApprovalChoice decision;
        try
        {
            decision = await _permissionApprovalPrompt.PromptAsync(
                new PermissionApprovalRequest(
                    executionContext.Session.ApplicationName,
                    request,
                    permissionResult.Reason ?? $"Permission approval is required for '{executionContext.ToolName}'."),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return PermissionEvaluationResult.Denied(
                "permission_request_cancelled",
                $"Permission approval was cancelled for tool '{executionContext.ToolName}'.",
                PermissionMode.Deny,
                request);
        }

        switch (decision)
        {
            case PermissionApprovalChoice.AllowOnce:
                return _permissionEvaluator.Evaluate(
                    permissionPolicy,
                    new PermissionEvaluationContext(executionContext, approvalGranted: true));

            case PermissionApprovalChoice.AllowForAgent:
                executionContext.Session.AddPermissionOverride(CreateOverrideRule(
                    request,
                    PermissionMode.Allow));
                return _permissionEvaluator.Evaluate(
                    permissionPolicy,
                    new PermissionEvaluationContext(executionContext));

            case PermissionApprovalChoice.DenyForAgent:
                executionContext.Session.AddPermissionOverride(CreateOverrideRule(
                    request,
                    PermissionMode.Deny));
                return PermissionEvaluationResult.Denied(
                    "permission_denied_by_user",
                    $"Permission was denied for tool '{executionContext.ToolName}' on this agent.",
                    PermissionMode.Deny,
                    request);

            default:
                return PermissionEvaluationResult.Denied(
                    "permission_denied_by_user",
                    $"Permission was denied for tool '{executionContext.ToolName}'.",
                    PermissionMode.Deny,
                    request);
        }
    }

    private static PermissionRule CreateOverrideRule(
        PermissionRequestDescriptor request,
        PermissionMode mode)
    {
        return new PermissionRule
        {
            Mode = mode,
            Patterns = request.Subjects.ToArray(),
            Tools = [request.ToolKind]
        };
    }

    private static JsonElement ParseArguments(ConversationToolCall toolCall)
    {
        if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            throw new InvalidOperationException(
                $"Tool '{toolCall.Name}' must receive JSON-object arguments.");
        }

        using JsonDocument argumentsDocument = JsonDocument.Parse(toolCall.ArgumentsJson);
        if (argumentsDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Tool '{toolCall.Name}' must receive JSON-object arguments.");
        }

        return argumentsDocument.RootElement.Clone();
    }

    private sealed class DisabledLifecycleHookService : ILifecycleHookService
    {
        public static DisabledLifecycleHookService Instance { get; } = new();

        public Task<LifecycleHookRunResult> RunAsync(
            LifecycleHookContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LifecycleHookRunResult.Allowed());
        }
    }
}
