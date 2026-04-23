using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Configuration;

internal static class ApplicationSettingsFactory
{
    private static readonly PermissionRule[] BuiltInPermissionRules =
    [
        new PermissionRule
        {
            Tools = ["read"],
            Mode = PermissionMode.Allow
        },
        new PermissionRule
        {
            Tools = ["webfetch"],
            Mode = PermissionMode.Allow
        },
        new PermissionRule
        {
            Tools = ["lsp"],
            Mode = PermissionMode.Allow
        },
        new PermissionRule
        {
            Tools = ["bash"],
            Mode = PermissionMode.Ask
        },
        new PermissionRule
        {
            Tools = ["edit"],
            Mode = PermissionMode.Ask
        },
        new PermissionRule
        {
            Tools = ["task"],
            Mode = PermissionMode.Ask
        },
        new PermissionRule
        {
            Tools = ["external_directory"],
            Mode = PermissionMode.Ask
        },
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
        }
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
            DefaultMode = configured.DefaultMode,
            Rules = BuiltInPermissionRules
                .Concat(configuredRules)
                .Select(NormalizeRule)
                .ToArray()
        };
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
