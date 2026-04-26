using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Infrastructure.Configuration;

internal static class ApplicationSettingsFactory
{
    private static readonly string[] BuiltInSafeShellCommandPatterns =
    [
        "cargo test*",
        "dotnet build*",
        "dotnet test*",
        "npm test*",
        "pnpm test*"
    ];

    private static readonly string[] BuiltInDeniedShellCommandPatterns =
    [
        "chmod 777*",
        "curl*|*bash*",
        "curl*|*sh*",
        "dd if=*",
        "dd of=*",
        "del /f*",
        "del /s*",
        "docker system prune*",
        "format*",
        "git clean -fd*",
        "git push --force*",
        "git push -f*",
        "git reset --hard*",
        "Invoke-WebRequest*|*iex*",
        "irm*|*iex*",
        "iwr*|*iex*",
        "mkfs*",
        "rd /s*",
        "Remove-Item *-Force*",
        "Remove-Item *-Recurse*",
        "rm -fr*",
        "rm -rf*",
        "rmdir /s*",
        "Set-ExecutionPolicy*",
        "sudo*",
        "wget*|*bash*",
        "wget*|*sh*"
    ];

    public static ConversationSettings CreateConversationSettings(ApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ConversationOptions conversation = options.Conversation ?? new ConversationOptions();
        string? systemPrompt = string.IsNullOrWhiteSpace(conversation.SystemPrompt)
            ? null
            : conversation.SystemPrompt.Trim();
        TimeSpan requestTimeout = conversation.RequestTimeoutSeconds <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(conversation.RequestTimeoutSeconds);

        return new ConversationSettings(
            systemPrompt,
            requestTimeout,
            Math.Max(0, conversation.MaxHistoryTurns),
            Math.Max(0, conversation.MaxToolRoundsPerTurn));
    }

    public static ModelSelectionSettings CreateModelSelectionSettings(ApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ModelSelectionSettings(
            TimeSpan.FromSeconds(options.ModelSelection.CacheDurationSeconds));
    }

    public static PermissionSettings CreatePermissionSettings(ApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        PermissionSettings configured = options.Permissions ?? new PermissionSettings();
        PermissionRule[] configuredRules = (configured.Rules ?? [])
            .Where(static rule => rule is not null)
            .Select(NormalizeRule)
            .ToArray();

        return new PermissionSettings
        {
            AutoApproveAllTools = configured.AutoApproveAllTools,
            DefaultMode = configured.AutoApproveAllTools ? PermissionMode.Allow : configured.DefaultMode,
            FileDelete = configured.FileDelete,
            FileRead = configured.FileRead,
            FileWrite = configured.FileWrite,
            McpTools = configured.McpTools,
            MemoryWrite = configured.MemoryWrite,
            Network = configured.Network,
            SandboxMode = configured.SandboxMode,
            Shell = configured.Shell ?? new ShellPermissionSettings(),
            ShellDefault = configured.ShellDefault,
            ShellSafe = configured.ShellSafe,
            Rules = CreateBuiltInPermissionRules(configured.AutoApproveAllTools)
                .Concat(CreateShortcutPermissionRules(configured))
                .Concat(configuredRules)
                .Select(NormalizeRule)
                .ToArray()
        };
    }

    private static PermissionRule[] CreateBuiltInPermissionRules(bool autoApproveAllTools)
    {
        PermissionMode promptableMode = autoApproveAllTools
            ? PermissionMode.Allow
            : PermissionMode.Ask;

        return
        [
            new PermissionRule
            {
                Tools = ["read"],
                Mode = PermissionMode.Allow
            },
            new PermissionRule
            {
                Tools = ["webfetch"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["lsp"],
                Mode = PermissionMode.Allow
            },
            new PermissionRule
            {
                Tools = ["bash"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = [AgentToolNames.FileWrite],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = [AgentToolNames.FileDelete],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = [AgentToolNames.ApplyPatch],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["edit"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["agent"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["task"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["mcp"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["external_directory"],
                Mode = promptableMode
            },
            new PermissionRule
            {
                Tools = ["sandbox"],
                Mode = promptableMode,
                Patterns = [ShellCommandSandboxArguments.SandboxEscalationSubject]
            },
            .. CreateShellCommandRules(PermissionMode.Allow, BuiltInSafeShellCommandPatterns),
            .. CreateAutoApproveAllToolsRules(autoApproveAllTools),
            new PermissionRule
            {
                Tools = ["doom_loop"],
                Mode = PermissionMode.Deny
            },
            new PermissionRule
            {
                Tools = ["read"],
                Mode = PermissionMode.Deny,
                Patterns = [".env", ".env.*", "**/.env", "**/.env.*"]
            },
            .. CreateShellCommandRules(PermissionMode.Deny, BuiltInDeniedShellCommandPatterns)
        ];
    }

    private static IEnumerable<PermissionRule> CreateAutoApproveAllToolsRules(bool autoApproveAllTools)
    {
        if (!autoApproveAllTools)
        {
            yield break;
        }

        yield return new PermissionRule
        {
            Mode = PermissionMode.Allow
        };
    }

    private static IEnumerable<PermissionRule> CreateShortcutPermissionRules(PermissionSettings configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        if (configured.FileRead is not null)
        {
            yield return CreateToolRule(configured.FileRead.Value, AgentToolNames.FileRead);
        }

        if (configured.FileWrite is not null)
        {
            yield return CreateToolRule(configured.FileWrite.Value, AgentToolNames.FileWrite);
        }

        if (configured.FileDelete is not null)
        {
            yield return CreateToolRule(configured.FileDelete.Value, AgentToolNames.FileDelete);
        }

        if (configured.ShellDefault is not null)
        {
            yield return CreateToolRule(configured.ShellDefault.Value, "bash");
        }

        if (configured.ShellSafe is not null)
        {
            foreach (PermissionRule rule in CreateShellCommandRules(
                         configured.ShellSafe.Value,
                         BuiltInSafeShellCommandPatterns))
            {
                yield return rule;
            }
        }

        if (configured.Network is not null)
        {
            yield return CreateToolRule(configured.Network.Value, "webfetch");
        }

        if (configured.MemoryWrite is not null)
        {
            yield return CreateToolRule(configured.MemoryWrite.Value, "memory_write");
        }

        if (configured.McpTools is not null)
        {
            yield return CreateToolRule(configured.McpTools.Value, "mcp");
        }

        ShellPermissionSettings shellSettings = configured.Shell ?? new ShellPermissionSettings();
        foreach (PermissionRule rule in CreateShellCommandRules(
                     PermissionMode.Allow,
                     shellSettings.Allow?.Commands ?? []))
        {
            yield return rule;
        }

        foreach (PermissionRule rule in CreateShellCommandRules(
                     PermissionMode.Deny,
                     shellSettings.Deny?.Commands ?? []))
        {
            yield return rule;
        }
    }

    private static PermissionRule CreateToolRule(
        PermissionMode mode,
        string tool)
    {
        return new PermissionRule
        {
            Mode = mode,
            Tools = [tool]
        };
    }

    private static IEnumerable<PermissionRule> CreateShellCommandRules(
        PermissionMode mode,
        IEnumerable<string> commands)
    {
        foreach (string pattern in NormalizeShellCommandPatterns(commands))
        {
            yield return new PermissionRule
            {
                Tools = ["bash"],
                Mode = mode,
                Patterns = [pattern]
            };
        }
    }

    private static IEnumerable<string> NormalizeShellCommandPatterns(IEnumerable<string> commands)
    {
        return commands
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Select(NormalizeShellCommandPattern)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeShellCommandPattern(string command)
    {
        string pattern = command.Trim();
        if (pattern.Contains('*', StringComparison.Ordinal) ||
            pattern.Contains('?', StringComparison.Ordinal))
        {
            return pattern;
        }

        if (!pattern.Contains('|', StringComparison.Ordinal))
        {
            return pattern + "*";
        }

        string[] pipeSegments = pattern
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pipeSegments.Length <= 1
            ? pattern + "*"
            : string.Join("*|*", pipeSegments) + "*";
    }

    private static PermissionRule NormalizeRule(PermissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new PermissionRule
        {
            Mode = rule.Mode,
            Patterns = (rule.Patterns ?? [])
                .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(static pattern => pattern.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Tools = (rule.Tools ?? [])
                .Where(static tool => !string.IsNullOrWhiteSpace(tool))
                .Select(static tool => tool.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
