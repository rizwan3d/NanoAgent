using System.Text.Json;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Plugins;

internal static class PluginJson
{
    public static string CreatePermissionRequirements(
        string pluginName,
        string toolName,
        ToolApprovalMode approvalMode,
        string? webRequestArgumentName = null)
    {
        string approvalModeName = approvalMode == ToolApprovalMode.Automatic
            ? nameof(ToolApprovalMode.Automatic)
            : nameof(ToolApprovalMode.RequireApproval);
        string webRequestPolicy = string.IsNullOrWhiteSpace(webRequestArgumentName)
            ? string.Empty
            : "," + Environment.NewLine +
              "  \"webRequest\": {" + Environment.NewLine +
              $"    \"requestArgumentName\": \"{Escape(webRequestArgumentName.Trim())}\"" + Environment.NewLine +
              "  }";

        return
            "{" + Environment.NewLine +
            $"  \"approvalMode\": \"{approvalModeName}\"," + Environment.NewLine +
            $"  \"toolTags\": [\"plugin\", \"plugin:{Escape(pluginName)}\", \"plugin:{Escape(pluginName)}:{Escape(toolName)}\"]{webRequestPolicy}" + Environment.NewLine +
            "}";
    }

    private static string Escape(string value)
    {
        return JsonEncodedText.Encode(value).ToString();
    }
}
