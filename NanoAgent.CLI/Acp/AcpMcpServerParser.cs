using NanoAgent.Application.Backend;
using System.Text.Json;

namespace NanoAgent.CLI;

internal static class AcpMcpServerParser
{
    private static readonly string[] NestedConfigProperties =
    [
        "config",
        "configuration",
        "session",
        "sessionConfig"
    ];

    public static IReadOnlyList<BackendMcpServerConfiguration> Parse(
        JsonElement parameters,
        string source)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        List<BackendMcpServerConfiguration> servers = [];
        AppendFromContainer(parameters, source, servers);

        foreach (string propertyName in NestedConfigProperties)
        {
            if (TryGetProperty(parameters, propertyName, out JsonElement nested) &&
                nested.ValueKind == JsonValueKind.Object)
            {
                AppendFromContainer(nested, source, servers);
            }
        }

        return servers;
    }

    private static void AppendFromContainer(
        JsonElement container,
        string source,
        List<BackendMcpServerConfiguration> servers)
    {
        if (!TryGetProperty(container, "mcpServers", out JsonElement mcpServers))
        {
            return;
        }

        if (mcpServers.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in mcpServers.EnumerateArray())
            {
                AddServer(item, fallbackName: null, source, servers);
            }

            return;
        }

        if (mcpServers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (LooksLikeServerObject(mcpServers))
        {
            AddServer(mcpServers, fallbackName: null, source, servers);
            return;
        }

        foreach (JsonProperty item in mcpServers.EnumerateObject())
        {
            AddServer(item.Value, item.Name, source, servers);
        }
    }

    private static void AddServer(
        JsonElement element,
        string? fallbackName,
        string source,
        List<BackendMcpServerConfiguration> servers)
    {
        BackendMcpServerConfiguration? server = ParseServer(element, fallbackName, source);
        if (server is not null)
        {
            servers.Add(server);
        }
    }

    private static BackendMcpServerConfiguration? ParseServer(
        JsonElement element,
        string? fallbackName,
        string source)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? name = fallbackName;
        if (TryReadString(element, "name", out string? configuredName) &&
            !string.IsNullOrWhiteSpace(configuredName))
        {
            name = configuredName;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        BackendMcpServerConfiguration server = new(name)
        {
            Source = source
        };

        string? type = TryReadString(element, "type", out string? configuredType)
            ? configuredType
            : null;
        bool commandAssigned = TryApplyString(
            element,
            server,
            "command",
            nameof(BackendMcpServerConfiguration.Command),
            static (target, value) => target.Command = value);
        bool urlAssigned = TryApplyString(
            element,
            server,
            "url",
            nameof(BackendMcpServerConfiguration.Url),
            static (target, value) => target.Url = value);

        if ((commandAssigned || IsTransport(type, "stdio")) && !urlAssigned)
        {
            server.Url = null;
            server.Mark(nameof(BackendMcpServerConfiguration.Url));
        }

        if ((urlAssigned || IsTransport(type, "http") || IsTransport(type, "sse")) && !commandAssigned)
        {
            server.Command = null;
            server.Mark(nameof(BackendMcpServerConfiguration.Command));
        }

        bool argsAssigned = TryApplyStringList(
            element,
            server,
            "args",
            nameof(BackendMcpServerConfiguration.Args),
            static target => target.Args);
        if (!argsAssigned && (commandAssigned || IsTransport(type, "stdio")))
        {
            server.Args.Clear();
            server.Mark(nameof(BackendMcpServerConfiguration.Args));
        }

        TryApplyString(
            element,
            server,
            "cwd",
            nameof(BackendMcpServerConfiguration.Cwd),
            static (target, value) => target.Cwd = value);
        TryApplyString(
            element,
            server,
            "bearerTokenEnvVar",
            nameof(BackendMcpServerConfiguration.BearerTokenEnvVar),
            static (target, value) => target.BearerTokenEnvVar = value);
        TryApplyString(
            element,
            server,
            "defaultToolsApprovalMode",
            nameof(BackendMcpServerConfiguration.DefaultToolsApprovalMode),
            static (target, value) => target.DefaultToolsApprovalMode = value);

        TryApplyDictionary(
            element,
            server,
            "env",
            nameof(BackendMcpServerConfiguration.Env),
            static target => target.Env);
        TryApplyDictionary(
            element,
            server,
            "headers",
            nameof(BackendMcpServerConfiguration.HttpHeaders),
            static target => target.HttpHeaders);
        TryApplyDictionary(
            element,
            server,
            "httpHeaders",
            nameof(BackendMcpServerConfiguration.HttpHeaders),
            static target => target.HttpHeaders);
        TryApplyDictionary(
            element,
            server,
            "envHttpHeaders",
            nameof(BackendMcpServerConfiguration.EnvHttpHeaders),
            static target => target.EnvHttpHeaders);
        TryApplyStringList(
            element,
            server,
            "envVars",
            nameof(BackendMcpServerConfiguration.EnvVars),
            static target => target.EnvVars);
        TryApplyStringList(
            element,
            server,
            "enabledTools",
            nameof(BackendMcpServerConfiguration.EnabledTools),
            static target => target.EnabledTools);
        TryApplyStringList(
            element,
            server,
            "disabledTools",
            nameof(BackendMcpServerConfiguration.DisabledTools),
            static target => target.DisabledTools);
        TryApplyBool(
            element,
            server,
            "enabled",
            nameof(BackendMcpServerConfiguration.Enabled),
            static (target, value) => target.Enabled = value);
        TryApplyBool(
            element,
            server,
            "required",
            nameof(BackendMcpServerConfiguration.Required),
            static (target, value) => target.Required = value);
        TryApplyPositiveInt(
            element,
            server,
            "startupTimeoutSeconds",
            nameof(BackendMcpServerConfiguration.StartupTimeoutSeconds),
            static (target, value) => target.StartupTimeoutSeconds = value);
        TryApplyPositiveInt(
            element,
            server,
            "toolTimeoutSeconds",
            nameof(BackendMcpServerConfiguration.ToolTimeoutSeconds),
            static (target, value) => target.ToolTimeoutSeconds = value);
        TryApplyDictionary(
            element,
            server,
            "toolApprovalModes",
            nameof(BackendMcpServerConfiguration.ToolApprovalModes),
            static target => target.ToolApprovalModes);
        TryApplyToolApprovalModes(element, server);

        return server;
    }

    private static bool LooksLikeServerObject(JsonElement element)
    {
        return HasAnyProperty(
            element,
            [
                "name",
                "type",
                "command",
                "args",
                "env",
                "url",
                "headers",
                "httpHeaders",
                "enabled",
                "required",
                "enabledTools",
                "disabledTools"
            ]);
    }

    private static bool HasAnyProperty(JsonElement element, IReadOnlyList<string> propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryApplyString(
        JsonElement element,
        BackendMcpServerConfiguration server,
        string jsonPropertyName,
        string configurationPropertyName,
        Action<BackendMcpServerConfiguration, string?> assign)
    {
        if (!TryReadString(element, jsonPropertyName, out string? value))
        {
            return false;
        }

        assign(server, NormalizeOptional(value));
        server.Mark(configurationPropertyName);
        return true;
    }

    private static bool TryApplyStringList(
        JsonElement element,
        BackendMcpServerConfiguration server,
        string jsonPropertyName,
        string configurationPropertyName,
        Func<BackendMcpServerConfiguration, List<string>> getTarget)
    {
        if (!TryGetProperty(element, jsonPropertyName, out JsonElement property))
        {
            return false;
        }

        List<string> target = getTarget(server);
        target.Clear();

        if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in property.EnumerateArray())
            {
                AddStringValue(target, item);
            }
        }
        else
        {
            AddStringValue(target, property);
        }

        server.Mark(configurationPropertyName);
        return true;
    }

    private static bool TryApplyDictionary(
        JsonElement element,
        BackendMcpServerConfiguration server,
        string jsonPropertyName,
        string configurationPropertyName,
        Func<BackendMcpServerConfiguration, Dictionary<string, string>> getTarget)
    {
        if (!TryGetProperty(element, jsonPropertyName, out JsonElement property))
        {
            return false;
        }

        Dictionary<string, string> target = getTarget(server);

        if (property.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty item in property.EnumerateObject())
            {
                string? value = GetScalarValue(item.Value);
                if (!string.IsNullOrWhiteSpace(item.Name) && value is not null)
                {
                    target[item.Name.Trim()] = value;
                }
            }
        }
        else if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !TryReadString(item, "name", out string? name) ||
                    string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string value = TryReadString(item, "value", out string? itemValue)
                    ? itemValue ?? string.Empty
                    : string.Empty;
                target[name.Trim()] = value;
            }
        }

        server.Mark(configurationPropertyName);
        return true;
    }

    private static bool TryApplyBool(
        JsonElement element,
        BackendMcpServerConfiguration server,
        string jsonPropertyName,
        string configurationPropertyName,
        Action<BackendMcpServerConfiguration, bool> assign)
    {
        if (!TryGetProperty(element, jsonPropertyName, out JsonElement property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return false;
        }

        assign(server, property.GetBoolean());
        server.Mark(configurationPropertyName);
        return true;
    }

    private static bool TryApplyPositiveInt(
        JsonElement element,
        BackendMcpServerConfiguration server,
        string jsonPropertyName,
        string configurationPropertyName,
        Action<BackendMcpServerConfiguration, int> assign)
    {
        if (!TryGetProperty(element, jsonPropertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out int value) ||
            value <= 0)
        {
            return false;
        }

        assign(server, value);
        server.Mark(configurationPropertyName);
        return true;
    }

    private static void TryApplyToolApprovalModes(
        JsonElement element,
        BackendMcpServerConfiguration server)
    {
        if (!TryGetProperty(element, "tools", out JsonElement tools) ||
            tools.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty item in tools.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(item.Name) ||
                item.Value.ValueKind != JsonValueKind.Object ||
                !TryReadString(item.Value, "approvalMode", out string? approvalMode) ||
                string.IsNullOrWhiteSpace(approvalMode))
            {
                continue;
            }

            server.ToolApprovalModes[item.Name.Trim()] = approvalMode.Trim();
            server.Mark(nameof(BackendMcpServerConfiguration.ToolApprovalModes));
        }
    }

    private static void AddStringValue(List<string> values, JsonElement element)
    {
        string? value = GetScalarValue(element);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static bool IsTransport(string? type, string expected)
    {
        return string.Equals(type?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        return element.TryGetProperty(propertyName, out property);
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        value = GetScalarValue(property);
        return value is not null;
    }

    private static string? GetScalarValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
