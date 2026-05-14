using FluentAssertions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Telemetry;

namespace NanoAgent.Tests.Infrastructure.Telemetry;

public sealed class ProductTelemetryHelpersTests
{
    [Fact]
    public void CreateFeatureProperties_ShouldOnlyIncludeAllowlistedTelemetryFields()
    {
        ConversationTurnMetrics metrics = new(
            TimeSpan.FromSeconds(12),
            estimatedOutputTokens: 320,
            estimatedInputTokens: 180,
            cachedInputTokens: 20,
            providerRetryCount: 1,
            toolRoundCount: 2);

        IReadOnlyDictionary<string, object> properties = ProductTelemetryHelpers.CreateFeatureProperties(
            "1.2.3",
            "windows",
            "vscode",
            "prompt_with_attachments",
            "turn",
            success: true,
            metrics,
            attachmentCount: 3,
            exception: null);

        properties.Keys.Should().BeEquivalentTo(
            [
                "nanoagent_version",
                "os_family",
                "app_surface",
                "feature_name",
                "interaction_kind",
                "success",
                "attachment_count_bucket",
                "duration_bucket",
                "total_token_bucket",
                "input_token_bucket",
                "output_token_bucket",
                "cached_input_token_bucket",
                "provider_retry_bucket",
                "tool_round_bucket"
            ]);
    }

    [Fact]
    public void CreateFeatureProperties_ShouldBucketMetricsAndSanitizeFailures()
    {
        ConversationTurnMetrics metrics = new(
            TimeSpan.FromSeconds(70),
            estimatedOutputTokens: 4_500,
            estimatedInputTokens: 1_250,
            cachedInputTokens: 0,
            providerRetryCount: 6,
            toolRoundCount: 0);

        IReadOnlyDictionary<string, object> properties = ProductTelemetryHelpers.CreateFeatureProperties(
            "1.2.3",
            "linux",
            "cli",
            "prompt",
            "turn",
            success: false,
            metrics,
            attachmentCount: 0,
            exception: new ConversationPipelineException(@"Failed for C:\repo\secret.txt"));

        properties["duration_bucket"].Should().Be("ge_60s");
        properties["total_token_bucket"].Should().Be("ge_4001");
        properties["input_token_bucket"].Should().Be("1001_to_4000");
        properties["output_token_bucket"].Should().Be("ge_4001");
        properties["provider_retry_bucket"].Should().Be("ge_6");
        properties["failure_kind"].Should().Be("conversation_pipeline");
        string serializedValues = string.Join("|", properties.Values.Select(static value => value?.ToString()));
        serializedValues.Should().NotContain(@"C:\repo\secret.txt");
    }

    [Fact]
    public void CreateAppStoppedProperties_ShouldBucketUsageTime()
    {
        IReadOnlyDictionary<string, object> properties = ProductTelemetryHelpers.CreateAppStoppedProperties(
            "1.2.3",
            "macos",
            "desktop",
            TimeSpan.FromMinutes(7));

        properties["usage_time_bucket"].Should().Be("5m_to_15m");
    }
}
