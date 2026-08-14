using StemCode.Application.Exceptions;
using StemCode.Application.Models;
using System.Text;

namespace StemCode.Infrastructure.Telemetry;

internal static class ProductTelemetryHelpers
{
    public static IReadOnlyDictionary<string, object> CreateIdentifyProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["$set"] = CreatePersonProperties(version, osFamily, appSurface, executionEnvironment, ciProvider)
        };
    }

    public static IReadOnlyDictionary<string, object> CreateAppStartedProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider)
    {
        return CreateCommonProperties(version, osFamily, appSurface, executionEnvironment, ciProvider);
    }

    public static IReadOnlyDictionary<string, object> CreateAppStoppedProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider,
        TimeSpan usageTime)
    {
        Dictionary<string, object> properties = CreateCommonProperties(
            version,
            osFamily,
            appSurface,
            executionEnvironment,
            ciProvider);
        properties["usage_time_bucket"] = ToUsageTimeBucket(usageTime);
        return properties;
    }

    public static IReadOnlyDictionary<string, object> CreateFeatureProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider,
        string featureName,
        string interactionKind,
        bool success,
        ConversationTurnMetrics? metrics,
        int attachmentCount,
        Exception? exception)
    {
        Dictionary<string, object> properties = CreateCommonProperties(
            version,
            osFamily,
            appSurface,
            executionEnvironment,
            ciProvider);
        properties["feature_name"] = NormalizeFeatureName(featureName);
        properties["interaction_kind"] = NormalizeInteractionKind(interactionKind);
        properties["success"] = success;
        properties["attachment_count_bucket"] = ToCountBucket(attachmentCount);

        if (metrics is not null)
        {
            properties["input_tokens"] = metrics.EstimatedInputTokens;
            properties["output_tokens"] = metrics.EstimatedOutputTokens;
            properties["total_tokens"] = metrics.EstimatedTotalTokens;
            properties["cached_input_tokens"] = metrics.CachedInputTokens;
            properties["duration_bucket"] = ToDurationBucket(metrics.Elapsed);
            properties["total_token_bucket"] = ToTokenBucket(metrics.EstimatedTotalTokens);
            properties["input_token_bucket"] = ToTokenBucket(metrics.EstimatedInputTokens);
            properties["output_token_bucket"] = ToTokenBucket(metrics.EstimatedOutputTokens);
            properties["cached_input_token_bucket"] = ToTokenBucket(metrics.CachedInputTokens);
            properties["provider_retry_bucket"] = ToCountBucket(metrics.ProviderRetryCount);
            properties["tool_round_bucket"] = ToCountBucket(metrics.ToolRoundCount);

            string? providerName = NormalizeTelemetryProviderName(metrics.ProviderName);
            if (providerName is not null)
            {
                properties["provider_name"] = providerName;
            }

            string? modelId = NormalizeTelemetryModelId(metrics.ModelId);
            if (modelId is not null)
            {
                properties["model_id"] = modelId;
            }
        }

        if (exception is not null)
        {
            properties["failure_kind"] = ToFailureKind(exception);
        }

        return properties;
    }

    public static IReadOnlyDictionary<string, object> CreateToolInvokedProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider,
        string toolName,
        ToolResultStatus status,
        bool success,
        TimeSpan duration,
        ConversationExecutionPhase executionPhase,
        string? modelId,
        string? providerName,
        string? errorMessage)
    {
        Dictionary<string, object> properties = CreateCommonProperties(
            version,
            osFamily,
            appSurface,
            executionEnvironment,
            ciProvider);
        properties["tool_name"] = NormalizeToolName(toolName);
        properties["tool_status"] = ToSnakeCase(status.ToString());
        properties["success"] = success;
        properties["duration_ms"] = (long)Math.Max(0, Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero));
        properties["duration_bucket"] = ToDurationBucket(duration);
        properties["execution_phase"] = executionPhase == ConversationExecutionPhase.Planning
            ? "planning"
            : "execution";

        string? normalizedProviderName = NormalizeTelemetryProviderName(providerName);
        if (normalizedProviderName is not null)
        {
            properties["provider_name"] = normalizedProviderName;
        }

        string? normalizedModelId = NormalizeTelemetryModelId(modelId);
        if (normalizedModelId is not null)
        {
            properties["model_id"] = normalizedModelId;
        }

        if (!success && !string.IsNullOrWhiteSpace(errorMessage))
        {
            properties["error_message"] = errorMessage!.Trim();
        }

        properties["tool_type"] = ResolveToolType(toolName);

        return properties;
    }

    public static IReadOnlyDictionary<string, object> CreateProviderRequestProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider,
        string providerName,
        bool success,
        TimeSpan latency,
        bool streamed,
        TimeSpan streamLatency,
        int retryCount,
        string? errorMessage)
    {
        Dictionary<string, object> properties = CreateCommonProperties(
            version,
            osFamily,
            appSurface,
            executionEnvironment,
            ciProvider);
        string? normalizedProviderName = NormalizeProviderKind(providerName);
        if (normalizedProviderName is not null)
        {
            properties["provider_name"] = normalizedProviderName;
        }

        properties["success"] = success;
        properties["latency_ms"] = (long)Math.Max(0, Math.Round(latency.TotalMilliseconds, MidpointRounding.AwayFromZero));
        properties["latency_bucket"] = ToDurationBucket(latency);
        properties["streamed"] = streamed;
        if (streamed)
        {
            properties["stream_latency_ms"] = (long)Math.Max(0, Math.Round(streamLatency.TotalMilliseconds, MidpointRounding.AwayFromZero));
            properties["stream_latency_bucket"] = ToDurationBucket(streamLatency);
        }

        properties["retry_count_bucket"] = ToCountBucket(retryCount);

        if (!success && !string.IsNullOrWhiteSpace(errorMessage))
        {
            properties["error_message"] = errorMessage!.Trim();
        }

        return properties;
    }

    public static string GetStemCodeVersion()
    {
        Type type = typeof(ProductTelemetryHelpers);
        return type.Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion
            ?? type.Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    public static string GetOsFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "other";
    }

    public static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "https://us.i.posthog.com";
        }

        return host.Trim().TrimEnd('/');
    }

    public static string ResolveTelemetryAppSurface(
        string appSurface,
        Func<string, string?> environmentVariableReader)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableReader);

        string normalizedAppSurface = string.IsNullOrWhiteSpace(appSurface)
            ? "cli"
            : appSurface.Trim().ToLowerInvariant();

        if (!string.Equals(normalizedAppSurface, "cli", StringComparison.Ordinal))
        {
            return normalizedAppSurface;
        }

        return DetectCiProvider(environmentVariableReader) switch
        {
            "github_actions" => "github_actions",
            "gitlab_ci" => "gitlab_ci",
            "bitbucket_pipelines" => "bitbucket_pipelines",
            "generic_ci" => "ci",
            _ => "cli"
        };
    }

    public static string ResolveExecutionEnvironment(string telemetryAppSurface)
    {
        return telemetryAppSurface switch
        {
            "github_actions" or "gitlab_ci" or "bitbucket_pipelines" or "ci" => "ci",
            _ => "local"
        };
    }

    public static string? DetectCiProvider(Func<string, string?> environmentVariableReader)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableReader);

        if (HasTruthyEnvironmentVariable(environmentVariableReader, "GITHUB_ACTIONS") ||
            HasEnvironmentVariable(environmentVariableReader, "GITHUB_RUN_ID"))
        {
            return "github_actions";
        }

        if (HasTruthyEnvironmentVariable(environmentVariableReader, "GITLAB_CI") ||
            HasEnvironmentVariable(environmentVariableReader, "CI_PIPELINE_ID"))
        {
            return "gitlab_ci";
        }

        if (HasEnvironmentVariable(environmentVariableReader, "BITBUCKET_BUILD_NUMBER") ||
            HasEnvironmentVariable(environmentVariableReader, "BITBUCKET_PIPELINE_UUID"))
        {
            return "bitbucket_pipelines";
        }

        if (HasTruthyEnvironmentVariable(environmentVariableReader, "CI"))
        {
            return "generic_ci";
        }

        return null;
    }

    private static Dictionary<string, object> CreatePersonProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider)
    {
        return CreateCommonProperties(version, osFamily, appSurface, executionEnvironment, ciProvider);
    }

    private static Dictionary<string, object> CreateCommonProperties(
        string version,
        string osFamily,
        string appSurface,
        string executionEnvironment,
        string? ciProvider)
    {
        Dictionary<string, object> properties = new(StringComparer.Ordinal)
        {
            ["app_version"] = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim(),
            ["os_family"] = string.IsNullOrWhiteSpace(osFamily) ? "other" : osFamily.Trim().ToLowerInvariant(),
            ["app_surface"] = string.IsNullOrWhiteSpace(appSurface) ? "cli" : appSurface.Trim().ToLowerInvariant(),
            ["execution_environment"] = string.IsNullOrWhiteSpace(executionEnvironment)
                ? "local"
                : executionEnvironment.Trim().ToLowerInvariant(),
            ["is_ci"] = string.Equals(executionEnvironment, "ci", StringComparison.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrWhiteSpace(ciProvider))
        {
            properties["ci_provider"] = ciProvider.Trim().ToLowerInvariant();
        }

        return properties;
    }

    private static bool HasEnvironmentVariable(
        Func<string, string?> environmentVariableReader,
        string variableName)
    {
        return !string.IsNullOrWhiteSpace(environmentVariableReader(variableName));
    }

    private static bool HasTruthyEnvironmentVariable(
        Func<string, string?> environmentVariableReader,
        string variableName)
    {
        string? value = environmentVariableReader(variableName);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFeatureName(string? featureName)
    {
        return string.IsNullOrWhiteSpace(featureName)
            ? "unknown"
            : featureName.Trim().ToLowerInvariant();
    }

    private static string NormalizeToolName(string? toolName)
    {
        return string.IsNullOrWhiteSpace(toolName)
            ? "unknown"
            : toolName.Trim().ToLowerInvariant();
    }

    private static string ResolveToolType(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return "unknown";
        }

        string normalized = toolName.Trim();
        if (normalized.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return "mcp";
        }

        if (normalized.StartsWith("custom__", StringComparison.Ordinal))
        {
            return "custom";
        }

        return normalized switch
        {
            "shell_command" => "shell",
            "apply_patch" => "patch",
            "file_write" or "file_read" or "insert_content" or "file_delete" or "directory_list" => "file",
            "search_files" or "text_search" or "codebase_index" or "code_intelligence" => "search",
            "web_search" or "headless_browser" => "web",
            "planning_mode" or "update_plan" => "planning",
            "repo_memory" or "lesson_memory" => "memory",
            "skill_load" => "skill",
            "agent_delegate" or "agent_orchestrate" => "agent",
            "ask_question" => "interactive",
            _ => "other"
        };
    }

    private static string? NormalizeProviderKind(string? providerName)
    {
        return string.IsNullOrWhiteSpace(providerName)
            ? null
            : ToSnakeCase(providerName.Trim());
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        StringBuilder builder = new(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 && char.IsUpper(current))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static string NormalizeInteractionKind(string? interactionKind)
    {
        return string.IsNullOrWhiteSpace(interactionKind)
            ? "other"
            : interactionKind.Trim().ToLowerInvariant();
    }

    private static string? NormalizeTelemetryProviderName(string? providerName)
    {
        return string.IsNullOrWhiteSpace(providerName)
            ? null
            : providerName.Trim().ToLowerInvariant();
    }

    private static string? NormalizeTelemetryModelId(string? modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : modelId.Trim();
    }

    private static string ToDurationBucket(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return "none";
        }

        if (elapsed < TimeSpan.FromSeconds(5))
        {
            return "lt_5s";
        }

        if (elapsed < TimeSpan.FromSeconds(15))
        {
            return "5s_to_15s";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "15s_to_60s";
        }

        return "ge_60s";
    }

    private static string ToUsageTimeBucket(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return "none";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "lt_1m";
        }

        if (elapsed < TimeSpan.FromMinutes(5))
        {
            return "1m_to_5m";
        }

        if (elapsed < TimeSpan.FromMinutes(15))
        {
            return "5m_to_15m";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return "15m_to_60m";
        }

        return "ge_60m";
    }

    private static string ToTokenBucket(int value)
    {
        if (value <= 0)
        {
            return "0";
        }

        if (value <= 200)
        {
            return "1_to_200";
        }

        if (value <= 1_000)
        {
            return "201_to_1000";
        }

        if (value <= 4_000)
        {
            return "1001_to_4000";
        }

        return "ge_4001";
    }

    private static string ToCountBucket(int value)
    {
        if (value <= 0)
        {
            return "0";
        }

        if (value == 1)
        {
            return "1";
        }

        if (value <= 5)
        {
            return "2_to_5";
        }

        return "ge_6";
    }

    private static string ToFailureKind(Exception exception)
    {
        return exception switch
        {
            PromptCancelledException => "prompt_cancelled",
            OperationCanceledException => "cancelled",
            ConversationPipelineException => "conversation_pipeline",
            ConversationProviderException => "provider_request",
            ConversationResponseException => "provider_response",
            HttpRequestException => "network",
            IOException => "io",
            UnauthorizedAccessException => "unauthorized",
            InvalidOperationException => "invalid_operation",
            ArgumentException => "invalid_argument",
            _ => "exception"
        };
    }
}
