using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class AgentDelegateTool : ITool
{
    private const int MaxTaskDescriptionLength = 80;

    private readonly IServiceProvider _serviceProvider;
    private readonly IAgentProfileResolver _profileResolver;
    private readonly ITokenEstimator _tokenEstimator;

    public AgentDelegateTool(
        IServiceProvider serviceProvider,
        IAgentProfileResolver profileResolver,
        ITokenEstimator tokenEstimator)
    {
        _serviceProvider = serviceProvider;
        _profileResolver = profileResolver;
        _tokenEstimator = tokenEstimator;
    }

    public string Description => "Delegate a focused task to an available subagent and return its handoff response.";

    public string Name => AgentToolNames.AgentDelegate;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["agent"]
        }
        """;

    public string Schema => CreateSchema();

    private string CreateSchema()
    {
        IReadOnlyList<IAgentProfile> subagentProfiles = ListSubagentProfiles();
        string subagentDescription = subagentProfiles.Count == 0
            ? "Subagent to invoke."
            : $"Subagent to invoke. Available subagents: {FormatProfileSummaries(subagentProfiles)}.";
        string enumValues = string.Join(
            ", ",
            subagentProfiles.Select(static profile => $"\"{EscapeJsonString(profile.Name)}\""));

        return $$"""
        {
          "type": "object",
          "properties": {
            "agent": {
              "type": "string",
              "description": "{{EscapeJsonString(subagentDescription)}}",
              "enum": [{{enumValues}}]
            },
            "task": {
              "type": "string",
              "description": "Focused, self-contained task for the subagent."
            },
            "context": {
              "type": "string",
              "description": "Optional concise context, constraints, files, or expected output for the subagent."
            }
          },
          "required": ["agent", "task"],
          "additionalProperties": false
        }
        """;
    }

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "agent", out string? agentName))
        {
            IReadOnlyList<IAgentProfile> subagentProfiles = ListSubagentProfiles();
            return ToolResultFactory.InvalidArguments(
                "missing_agent",
                "Tool 'agent_delegate' requires a non-empty 'agent' string.",
                new ToolRenderPayload(
                    "Invalid agent_delegate arguments",
                    $"Provide 'agent' as one of: {FormatProfileNames(subagentProfiles)}."));
        }

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "task", out string? task))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_task",
                "Tool 'agent_delegate' requires a non-empty 'task' string.",
                new ToolRenderPayload(
                    "Invalid agent_delegate arguments",
                    "Provide a focused, non-empty delegated task."));
        }

        IAgentProfile subagentProfile;
        try
        {
            subagentProfile = _profileResolver.Resolve(agentName);
        }
        catch (ArgumentException)
        {
            IReadOnlyList<IAgentProfile> subagentProfiles = ListSubagentProfiles();
            return ToolResultFactory.InvalidArguments(
                "unknown_subagent",
                $"Unknown subagent '{agentName}'. Available subagents: {FormatProfileNames(subagentProfiles)}.",
                new ToolRenderPayload(
                    $"Unknown subagent: {agentName}",
                    $"Use one of: {FormatProfileNames(subagentProfiles)}."));
        }

        if (subagentProfile.Mode != AgentProfileMode.Subagent)
        {
            IReadOnlyList<IAgentProfile> subagentProfiles = ListSubagentProfiles();
            return ToolResultFactory.InvalidArguments(
                "profile_is_not_subagent",
                $"Agent profile '{subagentProfile.Name}' is a primary profile and cannot be invoked through agent_delegate.",
                new ToolRenderPayload(
                    $"Not a subagent: {subagentProfile.Name}",
                    $"Use /profile to switch primary profiles, or delegate to one of: {FormatProfileNames(subagentProfiles)}."));
        }

        if (context.Session.AgentProfile.Mode != AgentProfileMode.Primary)
        {
            return ToolResultFactory.PermissionDenied(
                "subagent_cannot_delegate",
                $"Agent profile '{context.Session.AgentProfile.Name}' is not a primary profile and cannot delegate to another subagent.",
                new ToolRenderPayload(
                    "Subagent delegation blocked",
                    "Only primary profiles can invoke agent_delegate."));
        }

        if (context.Session.AgentProfile.PermissionIntent.EditMode == AgentProfileEditMode.ReadOnly &&
            subagentProfile.PermissionIntent.EditMode == AgentProfileEditMode.AllowEdits)
        {
            IReadOnlyList<IAgentProfile> readOnlySubagents = ListSubagentProfiles()
                .Where(static profile => profile.PermissionIntent.EditMode == AgentProfileEditMode.ReadOnly)
                .ToArray();
            return ToolResultFactory.PermissionDenied(
                "readonly_profile_cannot_delegate_edits",
                $"Agent profile '{context.Session.AgentProfile.Name}' is read-only and cannot delegate to editing subagent '{subagentProfile.Name}'.",
                new ToolRenderPayload(
                    "Editing subagent blocked",
                    $"Use a read-only subagent such as: {FormatProfileNames(readOnlySubagents)}."));
        }

        ReplSessionContext childSession = CreateChildSession(context.Session, subagentProfile);
        string? delegatedContext = ToolArguments.GetOptionalString(context.Arguments, "context");
        string delegatedInput = CreateDelegatedInput(context.Session, task!, delegatedContext);

        IConversationPipeline conversationPipeline = _serviceProvider.GetRequiredService<IConversationPipeline>();
        ConversationTurnResult turnResult = await conversationPipeline.ProcessAsync(
            delegatedInput,
            childSession,
            NoOpConversationProgressSink.Instance,
            cancellationToken);

        bool recordedFileEdits = TryRecordChildFileEdits(
            childSession,
            context.Session,
            subagentProfile.Name,
            task!);

        string[] executedTools = turnResult.ToolExecutionResult?.Results
            .Select(static result => result.ToolName)
            .Where(static tool => !string.IsNullOrWhiteSpace(tool))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        int estimatedOutputTokens = turnResult.Metrics?.EstimatedOutputTokens ??
            _tokenEstimator.Estimate(turnResult.ResponseText);

        AgentDelegationResult result = new(
            subagentProfile.Name,
            task!,
            turnResult.ResponseText,
            executedTools,
            estimatedOutputTokens,
            recordedFileEdits);

        return ToolResultFactory.Success(
            $"Subagent '{subagentProfile.Name}' completed the delegated task.",
            result,
            ToolJsonContext.Default.AgentDelegationResult,
            new ToolRenderPayload(
                $"Subagent {subagentProfile.Name} completed",
                CreateRenderText(result)));
    }

    private static ReplSessionContext CreateChildSession(
        ReplSessionContext parentSession,
        IAgentProfile subagentProfile)
    {
        ReplSessionContext childSession = new(
            parentSession.ApplicationName,
            parentSession.ProviderProfile,
            parentSession.ActiveModelId,
            parentSession.AvailableModelIds,
            agentProfile: subagentProfile,
            reasoningEffort: parentSession.ReasoningEffort);

        foreach (PermissionRule rule in parentSession.PermissionOverrides)
        {
            childSession.AddPermissionOverride(rule);
        }

        return childSession;
    }

    private static string CreateDelegatedInput(
        ReplSessionContext parentSession,
        string task,
        string? context)
    {
        string instructions =
            $"Delegated task from parent agent '{parentSession.AgentProfile.Name}'.{Environment.NewLine}{Environment.NewLine}" +
            $"Task:{Environment.NewLine}{task.Trim()}{Environment.NewLine}{Environment.NewLine}" +
            "Return your final response as a concise handoff to the parent agent.";

        if (string.IsNullOrWhiteSpace(context))
        {
            return instructions;
        }

        return
            $"{instructions}{Environment.NewLine}{Environment.NewLine}" +
            $"Additional context:{Environment.NewLine}{context.Trim()}";
    }

    private static bool TryRecordChildFileEdits(
        ReplSessionContext childSession,
        ReplSessionContext parentSession,
        string subagentName,
        string task)
    {
        if (!childSession.TryCreateFileEditTransactionSnapshot(
                $"subagent {subagentName}: {Truncate(task, MaxTaskDescriptionLength)}",
                out WorkspaceFileEditTransaction? transaction) ||
            transaction is null)
        {
            return false;
        }

        parentSession.RecordFileEditTransaction(transaction);
        return true;
    }

    private static string CreateRenderText(AgentDelegationResult result)
    {
        string text = result.Response;

        if (result.ExecutedTools.Count > 0)
        {
            text += $"{Environment.NewLine}{Environment.NewLine}Tools: {string.Join(", ", result.ExecutedTools)}";
        }

        if (result.RecordedFileEdits)
        {
            text += $"{Environment.NewLine}File edits were recorded for undo.";
        }

        return text;
    }

    private IReadOnlyList<IAgentProfile> ListSubagentProfiles()
    {
        return _profileResolver
            .List()
            .Where(static profile => profile.Mode == AgentProfileMode.Subagent)
            .ToArray();
    }

    private static string FormatProfileNames(IEnumerable<IAgentProfile> profiles)
    {
        return string.Join(", ", profiles.Select(static profile => profile.Name));
    }

    private static string FormatProfileSummaries(IReadOnlyList<IAgentProfile> profiles)
    {
        return string.Join(
            "; ",
            profiles.Select(static profile => $"{profile.Name} - {profile.Description}"));
    }

    private static string EscapeJsonString(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string Truncate(
        string value,
        int maxLength)
    {
        string normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private sealed class NoOpConversationProgressSink : IConversationProgressSink
    {
        public static NoOpConversationProgressSink Instance { get; } = new();

        public Task ReportExecutionPlanAsync(
            ExecutionPlanProgress executionPlanProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(executionPlanProgress);
            return Task.CompletedTask;
        }

        public Task ReportToolCallsStartedAsync(
            IReadOnlyList<ConversationToolCall> toolCalls,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ReportToolResultsAsync(
            ToolExecutionBatchResult toolExecutionResult,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(toolExecutionResult);
            return Task.CompletedTask;
        }
    }

}
