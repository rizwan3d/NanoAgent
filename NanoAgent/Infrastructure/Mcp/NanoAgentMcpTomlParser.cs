namespace NanoAgent.Infrastructure.Mcp;

internal static class NanoAgentMcpTomlParser
{
    public static IReadOnlyList<McpServerConfiguration> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Dictionary<string, McpServerConfiguration> servers = new(StringComparer.OrdinalIgnoreCase);
        McpServerConfiguration? currentServer = null;
        string[] currentSection = [];

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) &&
                line.EndsWith("]", StringComparison.Ordinal))
            {
                string[] tablePath = ParseDottedPath(line[1..^1]);
                if (tablePath.Length >= 2 &&
                    string.Equals(tablePath[0], "mcp_servers", StringComparison.Ordinal))
                {
                    currentServer = GetOrCreate(servers, tablePath[1], path);
                    currentSection = tablePath.Skip(2).ToArray();
                }
                else
                {
                    currentServer = null;
                    currentSection = [];
                }

                continue;
            }

            if (currentServer is null)
            {
                continue;
            }

            int equalsIndex = FindEquals(line);
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = UnquoteBareKey(line[..equalsIndex].Trim());
            string value = line[(equalsIndex + 1)..].Trim();
            ApplyValue(currentServer, currentSection, key, value);
        }

        return servers.Values.ToArray();
    }

    private static McpServerConfiguration GetOrCreate(
        Dictionary<string, McpServerConfiguration> servers,
        string serverName,
        string sourcePath)
    {
        if (!servers.TryGetValue(serverName, out McpServerConfiguration? server))
        {
            server = new McpServerConfiguration(serverName)
            {
                SourcePath = sourcePath
            };
            servers[serverName] = server;
        }

        server.SourcePath = sourcePath;
        return server;
    }

    private static void ApplyValue(
        McpServerConfiguration server,
        IReadOnlyList<string> section,
        string key,
        string value)
    {
        if (section.Count == 1 &&
            string.Equals(section[0], "env", StringComparison.Ordinal))
        {
            server.Env[key] = ParseString(value);
            server.Mark(nameof(McpServerConfiguration.Env));
            return;
        }

        if (section.Count == 2 &&
            string.Equals(section[0], "tools", StringComparison.Ordinal) &&
            string.Equals(key, "approval_mode", StringComparison.Ordinal))
        {
            server.ToolApprovalModes[section[1]] = ParseString(value);
            server.Mark(nameof(McpServerConfiguration.ToolApprovalModes));
            return;
        }

        if (section.Count != 0)
        {
            return;
        }

        switch (key)
        {
            case "command":
                server.Command = ParseString(value);
                server.Mark(nameof(McpServerConfiguration.Command));
                break;

            case "args":
                server.Args.Clear();
                server.Args.AddRange(ParseStringArray(value));
                server.Mark(nameof(McpServerConfiguration.Args));
                break;

            case "env":
                server.Env.Clear();
                foreach (KeyValuePair<string, string> item in ParseInlineTable(value))
                {
                    server.Env[item.Key] = item.Value;
                }

                server.Mark(nameof(McpServerConfiguration.Env));
                break;

            case "env_vars":
                server.EnvVars.Clear();
                server.EnvVars.AddRange(ParseEnvVars(value));
                server.Mark(nameof(McpServerConfiguration.EnvVars));
                break;

            case "cwd":
                server.Cwd = ParseString(value);
                server.Mark(nameof(McpServerConfiguration.Cwd));
                break;

            case "url":
                server.Url = ParseString(value);
                server.Mark(nameof(McpServerConfiguration.Url));
                break;

            case "bearer_token_env_var":
                server.BearerTokenEnvVar = ParseString(value);
                server.Mark(nameof(McpServerConfiguration.BearerTokenEnvVar));
                break;

            case "http_headers":
                server.HttpHeaders.Clear();
                foreach (KeyValuePair<string, string> item in ParseInlineTable(value))
                {
                    server.HttpHeaders[item.Key] = item.Value;
                }

                server.Mark(nameof(McpServerConfiguration.HttpHeaders));
                break;

            case "env_http_headers":
                server.EnvHttpHeaders.Clear();
                foreach (KeyValuePair<string, string> item in ParseInlineTable(value))
                {
                    server.EnvHttpHeaders[item.Key] = item.Value;
                }

                server.Mark(nameof(McpServerConfiguration.EnvHttpHeaders));
                break;

            case "startup_timeout_sec":
                if (TryParsePositiveInt(value, out int startupTimeoutSeconds))
                {
                    server.StartupTimeoutSeconds = startupTimeoutSeconds;
                    server.Mark(nameof(McpServerConfiguration.StartupTimeoutSeconds));
                }

                break;

            case "tool_timeout_sec":
                if (TryParsePositiveInt(value, out int toolTimeoutSeconds))
                {
                    server.ToolTimeoutSeconds = toolTimeoutSeconds;
                    server.Mark(nameof(McpServerConfiguration.ToolTimeoutSeconds));
                }

                break;

            case "enabled":
                server.Enabled = ParseBool(value);
                server.Mark(nameof(McpServerConfiguration.Enabled));
                break;

            case "required":
                server.Required = ParseBool(value);
                server.Mark(nameof(McpServerConfiguration.Required));
                break;

            case "enabled_tools":
                server.EnabledTools.Clear();
                server.EnabledTools.AddRange(ParseStringArray(value));
                server.Mark(nameof(McpServerConfiguration.EnabledTools));
                break;

            case "disabled_tools":
                server.DisabledTools.Clear();
                server.DisabledTools.AddRange(ParseStringArray(value));
                server.Mark(nameof(McpServerConfiguration.DisabledTools));
                break;

            case "default_tools_approval_mode":
                server.DefaultToolsApprovalMode = ParseString(value);
                server.Mark(nameof(McpServerConfiguration.DefaultToolsApprovalMode));
                break;
        }
    }

    private static string StripComment(string value)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inDoubleQuote && c == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && c == '#')
            {
                return value[..index];
            }
        }

        return value;
    }

    private static int FindEquals(string value)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inDoubleQuote && c == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && c == '=')
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] ParseDottedPath(string value)
    {
        List<string> segments = [];
        int index = 0;

        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index >= value.Length)
            {
                break;
            }

            string segment;
            if (value[index] is '"' or '\'')
            {
                char quote = value[index++];
                int start = index;
                bool escaped = false;
                while (index < value.Length)
                {
                    if (escaped)
                    {
                        escaped = false;
                        index++;
                        continue;
                    }

                    if (quote == '"' && value[index] == '\\')
                    {
                        escaped = true;
                        index++;
                        continue;
                    }

                    if (value[index] == quote)
                    {
                        break;
                    }

                    index++;
                }

                segment = ParseString(quote + value[start..Math.Min(index, value.Length)] + quote);
                if (index < value.Length && value[index] == quote)
                {
                    index++;
                }
            }
            else
            {
                int start = index;
                while (index < value.Length && value[index] != '.')
                {
                    index++;
                }

                segment = value[start..index].Trim();
            }

            if (!string.IsNullOrWhiteSpace(segment))
            {
                segments.Add(segment.Trim());
            }

            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index < value.Length && value[index] == '.')
            {
                index++;
            }
        }

        return segments.ToArray();
    }

    private static string ParseString(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1];
        }

        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return trimmed;
        }

        string body = trimmed[1..^1];
        return body
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);
    }

    private static string UnquoteBareKey(string value)
    {
        return ParseString(value).Trim();
    }

    private static IReadOnlyList<string> ParseStringArray(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return [];
        }

        return SplitTopLevel(trimmed[1..^1])
            .Select(static item => item.Trim())
            .Where(static item => item.Length > 0)
            .Select(ParseString)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static IReadOnlyList<string> ParseEnvVars(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return [];
        }

        List<string> names = [];
        foreach (string item in SplitTopLevel(trimmed[1..^1]))
        {
            string itemText = item.Trim();
            if (itemText.Length == 0)
            {
                continue;
            }

            if (itemText.StartsWith("{", StringComparison.Ordinal))
            {
                Dictionary<string, string> table = ParseInlineTable(itemText);
                if (table.TryGetValue("name", out string? name) &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }

                continue;
            }

            names.Add(ParseString(itemText));
        }

        return names
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, string> ParseInlineTable(string value)
    {
        Dictionary<string, string> results = new(StringComparer.Ordinal);
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return results;
        }

        foreach (string pair in SplitTopLevel(trimmed[1..^1]))
        {
            int equalsIndex = FindEquals(pair);
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = UnquoteBareKey(pair[..equalsIndex].Trim());
            string itemValue = ParseString(pair[(equalsIndex + 1)..].Trim());
            if (!string.IsNullOrWhiteSpace(key))
            {
                results[key] = itemValue;
            }
        }

        return results;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value)
    {
        List<string> parts = [];
        int start = 0;
        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inDoubleQuote && c == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (c is '[' or '{')
            {
                depth++;
                continue;
            }

            if (c is ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (c == ',' && depth == 0)
            {
                parts.Add(value[start..index]);
                start = index + 1;
            }
        }

        parts.Add(value[start..]);
        return parts;
    }

    private static bool ParseBool(string value)
    {
        return bool.TryParse(value.Trim(), out bool result) && result;
    }

    private static bool TryParsePositiveInt(string value, out int result)
    {
        if (int.TryParse(value.Trim(), out result) && result > 0)
        {
            return true;
        }

        result = 0;
        return false;
    }
}
