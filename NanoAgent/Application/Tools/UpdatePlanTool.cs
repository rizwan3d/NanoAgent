using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal sealed class UpdatePlanTool : ITool
{
    private const string PendingStatus = "pending";
    private const string InProgressStatus = "in_progress";
    private const string CompletedStatus = "completed";

    public string Description =>
        "Publish or update a concise task plan for the current turn. Use this for multi-step work to show pending, in_progress, and completed steps. Keep at most one step in_progress.";

    public string Name => AgentToolNames.UpdatePlan;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "bypassUserPermissionRules": true
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "explanation": {
              "type": "string",
              "description": "Optional short reason for creating or revising the plan."
            },
            "plan": {
              "type": "array",
              "description": "Ordered task list. Completed steps must come first, then at most one in_progress step, then pending steps.",
              "minItems": 1,
              "items": {
                "type": "object",
                "properties": {
                  "step": {
                    "type": "string",
                    "description": "A concise, specific task step."
                  },
                  "status": {
                    "type": "string",
                    "enum": ["pending", "in_progress", "completed"],
                    "description": "Current status for this step."
                  }
                },
                "required": ["step", "status"],
                "additionalProperties": false
              }
            }
          },
          "required": ["plan"],
          "additionalProperties": false
        }
        """;

    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.Arguments.TryGetProperty("plan", out JsonElement planElement) ||
            planElement.ValueKind != JsonValueKind.Array ||
            planElement.GetArrayLength() == 0)
        {
            return Task.FromResult(CreateInvalidArguments(
                "missing_plan",
                "Tool 'update_plan' requires a non-empty 'plan' array."));
        }

        string? explanation = ToolArguments.GetOptionalString(context.Arguments, "explanation");
        List<PlanUpdateItem> plan = [];
        int completedCount = 0;
        int inProgressCount = 0;
        int pendingCount = 0;
        PlanStatusOrder? previousOrder = null;

        foreach (JsonElement itemElement in planElement.EnumerateArray())
        {
            if (itemElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(CreateInvalidArguments(
                    "invalid_plan_item",
                    "Every update_plan item must be a JSON object with 'step' and 'status'."));
            }

            if (!ToolArguments.TryGetNonEmptyString(itemElement, "step", out string? step))
            {
                return Task.FromResult(CreateInvalidArguments(
                    "missing_plan_step",
                    "Every update_plan item requires a non-empty 'step' string."));
            }

            if (!ToolArguments.TryGetNonEmptyString(itemElement, "status", out string? status))
            {
                return Task.FromResult(CreateInvalidArguments(
                    "missing_plan_status",
                    "Every update_plan item requires a non-empty 'status' string."));
            }

            string normalizedStatus = NormalizeStatus(status!);
            if (!IsKnownStatus(normalizedStatus))
            {
                return Task.FromResult(CreateInvalidArguments(
                    "invalid_plan_status",
                    "Plan item status must be one of: pending, in_progress, completed."));
            }

            PlanStatusOrder currentOrder = GetStatusOrder(normalizedStatus);
            if (previousOrder is not null && currentOrder < previousOrder)
            {
                return Task.FromResult(CreateInvalidArguments(
                    "invalid_plan_order",
                    "Plan statuses must stay ordered: completed steps first, then at most one in_progress step, then pending steps."));
            }

            previousOrder = currentOrder;

            switch (normalizedStatus)
            {
                case CompletedStatus:
                    completedCount++;
                    break;
                case InProgressStatus:
                    inProgressCount++;
                    break;
                default:
                    pendingCount++;
                    break;
            }

            if (inProgressCount > 1)
            {
                return Task.FromResult(CreateInvalidArguments(
                    "multiple_in_progress_steps",
                    "Only one update_plan item can be in_progress at a time."));
            }

            plan.Add(new PlanUpdateItem(step!, normalizedStatus));
        }

        PlanUpdateResult result = new(
            explanation,
            plan,
            completedCount,
            inProgressCount,
            pendingCount);

        return Task.FromResult(ToolResultFactory.Success(
            $"Plan updated: {completedCount} completed, {inProgressCount} in progress, {pendingCount} pending.",
            result,
            ToolJsonContext.Default.PlanUpdateResult,
            new ToolRenderPayload(
                "Plan updated",
                BuildRenderText(explanation, plan))));
    }

    private static ToolResult CreateInvalidArguments(
        string code,
        string message)
    {
        return ToolResultFactory.InvalidArguments(
            code,
            message,
            new ToolRenderPayload(
                "Invalid update_plan arguments",
                message));
    }

    private static string BuildRenderText(
        string? explanation,
        IReadOnlyList<PlanUpdateItem> plan)
    {
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            lines.Add(explanation.Trim());
        }

        lines.AddRange(plan.Select(static item => $"{ToMarker(item.Status)} {item.Step}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeStatus(string status)
    {
        return status
            .Trim()
            .Replace('-', '_')
            .ToLowerInvariant();
    }

    private static bool IsKnownStatus(string status)
    {
        return status is PendingStatus or InProgressStatus or CompletedStatus;
    }

    private static PlanStatusOrder GetStatusOrder(string status)
    {
        return status switch
        {
            CompletedStatus => PlanStatusOrder.Completed,
            InProgressStatus => PlanStatusOrder.InProgress,
            _ => PlanStatusOrder.Pending
        };
    }

    private static string ToMarker(string status)
    {
        return status switch
        {
            CompletedStatus => "✓",
            InProgressStatus => "☐",
            _ => "☐"
        };
    }

    private enum PlanStatusOrder
    {
        Completed = 0,
        InProgress = 1,
        Pending = 2
    }
}
