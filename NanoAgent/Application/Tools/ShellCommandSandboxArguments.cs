using NanoAgent.Application.Tools.Models;
using System.Text.Json;

namespace NanoAgent.Application.Tools;

internal static class ShellCommandSandboxArguments
{
    public const string UseDefaultValue = "use_default";
    public const string RequireEscalatedValue = "require_escalated";
    public const string SandboxEscalationSubject = "sandbox:require_escalated";

    public static bool TryGetSandboxPermissions(
        JsonElement arguments,
        string argumentName,
        out ShellCommandSandboxPermissions sandboxPermissions,
        out string? invalidValue)
    {
        sandboxPermissions = ShellCommandSandboxPermissions.UseDefault;
        invalidValue = null;

        if (!ToolArguments.TryGetString(arguments, argumentName, out string? value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, UseDefaultValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, RequireEscalatedValue, StringComparison.OrdinalIgnoreCase))
        {
            sandboxPermissions = ShellCommandSandboxPermissions.RequireEscalated;
            return true;
        }

        invalidValue = value;
        return false;
    }

    public static string ToWireValue(ShellCommandSandboxPermissions sandboxPermissions)
    {
        return sandboxPermissions switch
        {
            ShellCommandSandboxPermissions.RequireEscalated => RequireEscalatedValue,
            _ => UseDefaultValue
        };
    }
}
