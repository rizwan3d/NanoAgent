using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class SkillLoadTool : ITool
{
    private readonly ISkillService _skillService;

    public SkillLoadTool(ISkillService skillService)
    {
        _skillService = skillService;
    }

    public string Description => "Load the full body instructions for a workspace skill after its name and description indicate that it is relevant.";

    public string Name => AgentToolNames.SkillLoad;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["read", "skill"]
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "Name of the workspace skill to load."
            }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolArguments.TryGetNonEmptyString(context.Arguments, "name", out string? name))
        {
            return ToolResultFactory.InvalidArguments(
                "missing_skill_name",
                "Tool 'skill_load' requires a non-empty 'name' string.",
                new ToolRenderPayload(
                    "Invalid skill_load arguments",
                    "Provide a non-empty skill name."));
        }

        WorkspaceSkillLoadResult? result = await _skillService.LoadAsync(
            context.Session,
            name!,
            cancellationToken);
        if (result is null)
        {
            return ToolResultFactory.NotFound(
                "skill_not_found",
                $"Workspace skill '{name}' was not found.",
                new ToolRenderPayload(
                    "Skill not found",
                    $"No workspace skill named '{name}' is available."));
        }

        string renderText = result.WasTruncated
            ? result.Instructions + $"{Environment.NewLine}{Environment.NewLine}[Skill instructions truncated by NanoAgent.]"
            : result.Instructions;

        return ToolResultFactory.Success(
            $"Loaded workspace skill '{result.Name}'.",
            result,
            ToolJsonContext.Default.WorkspaceSkillLoadResult,
            new ToolRenderPayload(
                $"Skill: {result.Name}",
                renderText));
    }
}
