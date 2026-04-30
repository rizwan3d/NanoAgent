using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using System.Text;

namespace NanoAgent.Application.Permissions;

internal static class PermissionRequestDisplayFormatter
{
    public static string BuildApprovalTitle(PermissionRequestDescriptor request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ToolName switch
        {
            AgentToolNames.ApplyPatch => "Approve patch changes?",
            AgentToolNames.DirectoryList => "Approve directory listing?",
            AgentToolNames.FileDelete => "Approve file delete?",
            AgentToolNames.FileRead => "Approve file read?",
            AgentToolNames.FileWrite => "Approve file write?",
            AgentToolNames.HeadlessBrowser => "Approve browser request?",
            AgentToolNames.SearchFiles => "Approve file search?",
            AgentToolNames.ShellCommand => "Approve shell command?",
            AgentToolNames.TextSearch => "Approve text search?",
            AgentToolNames.WebRun => "Approve web request?",
            _ => $"Approve {request.ToolName}?"
        };
    }

    public static string BuildDecisionMessage(
        PermissionRequestDescriptor request,
        string verb)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);

        if (request.Subjects.Count == 0)
        {
            return $"Permission {verb} tool '{request.ToolName}'.";
        }

        string action = request.ToolName switch
        {
            AgentToolNames.ApplyPatch => "modify patch target",
            AgentToolNames.DirectoryList => "list path",
            AgentToolNames.FileDelete => "delete file",
            AgentToolNames.FileRead => "read file",
            AgentToolNames.FileWrite => "write file",
            AgentToolNames.HeadlessBrowser => "open browser target",
            AgentToolNames.SearchFiles => "search path",
            AgentToolNames.ShellCommand => "run command",
            AgentToolNames.TextSearch => "search text in path",
            AgentToolNames.WebRun => "send request",
            _ => "access target"
        };

        return request.Subjects.Count == 1
            ? $"Permission {verb} tool '{request.ToolName}' to {action} '{request.Subjects[0]}'."
            : $"Permission {verb} tool '{request.ToolName}' to {action} for {request.Subjects.Count} targets.";
    }

    public static string BuildPromptDescription(PermissionApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        StringBuilder builder = new();
        builder.Append(request.Reason);
        builder.AppendLine();
        builder.AppendLine();
        builder.Append("Tool: ");
        builder.Append(request.Request.ToolName);

        foreach (string line in BuildSubjectLines(request.Request))
        {
            builder.AppendLine();
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildSubjectLines(PermissionRequestDescriptor request)
    {
        if (request.Subjects.Count == 0)
        {
            return ["Target: this tool request"];
        }

        string singularLabel;
        string pluralLabel;

        switch (request.ToolName)
        {
            case AgentToolNames.ApplyPatch:
                singularLabel = "Patch target";
                pluralLabel = "Patch targets";
                break;

            case AgentToolNames.ShellCommand:
                singularLabel = "Command";
                pluralLabel = "Commands";
                break;

            case AgentToolNames.FileRead:
            case AgentToolNames.FileDelete:
            case AgentToolNames.FileWrite:
                singularLabel = "File path";
                pluralLabel = "File paths";
                break;

            case AgentToolNames.WebRun:
            case AgentToolNames.HeadlessBrowser:
                singularLabel = "Web target";
                pluralLabel = "Web targets";
                break;

            default:
                singularLabel = "Path";
                pluralLabel = "Paths";
                break;
        }

        return request.Subjects.Count == 1
            ? [$"{singularLabel}: {request.Subjects[0]}"]
            : [pluralLabel + ":", .. request.Subjects.Select(static subject => $"- {subject}")];
    }
}
