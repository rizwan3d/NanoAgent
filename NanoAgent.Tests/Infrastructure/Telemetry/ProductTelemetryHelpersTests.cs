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
            toolRoundCount: 2,
            providerName: "OpenAI",
            modelId: "gpt-5");

        IReadOnlyDictionary<string, object> properties = ProductTelemetryHelpers.CreateFeatureProperties(
            "1.2.3",
            "windows",
            "vscode",
            "local",
            ciProvider: null,
            "prompt_with_attachments",
            "turn",
            success: true,
            metrics,
            attachmentCount: 3,
            exception: null);

        properties.Keys.Should().BeEquivalentTo(
            [
                "app_version",
                "os_family",
                "app_surface",
                "execution_environment",
                "is_ci",
                "feature_name",
                "interaction_kind",
                "success",
                "attachment_count_bucket",
                "input_tokens",
                "output_tokens",
                "total_tokens",
                "cached_input_tokens",
                "duration_bucket",
                "total_token_bucket",
                "input_token_bucket",
                "output_token_bucket",
                "cached_input_token_bucket",
                "provider_retry_bucket",
                "tool_round_bucket",
                "provider_name",
                "model_id"
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
            "local",
            ciProvider: null,
            "prompt",
            "turn",
            success: false,
            metrics,
            attachmentCount: 0,
            exception: new ConversationPipelineException(@"Failed for C:\repo\secret.txt"));

        properties["duration_bucket"].Should().Be("ge_60s");
        properties["input_tokens"].Should().Be(1250);
        properties["output_tokens"].Should().Be(4500);
        properties["total_tokens"].Should().Be(5750);
        properties["total_token_bucket"].Should().Be("ge_4001");
        properties["input_token_bucket"].Should().Be("1001_to_4000");
        properties["output_token_bucket"].Should().Be("ge_4001");
        properties["provider_retry_bucket"].Should().Be("ge_6");
        properties["failure_kind"].Should().Be("conversation_pipeline");
        string serializedValues = string.Join("|", properties.Values.Select(static value => value?.ToString()));
        serializedValues.Should().NotContain(@"C:\repo\secret.txt");
    }

    [Fact]
    public void CreateFeatureProperties_ShouldIncludeNormalizedProviderAndModel()
    {
        ConversationTurnMetrics metrics = new(
            TimeSpan.FromSeconds(4),
            estimatedOutputTokens: 80,
            estimatedInputTokens: 120,
            cachedInputTokens: 30,
            providerName: "OpenAI ChatGPT Plus/Pro",
            modelId: "gpt-5-mini");

        IReadOnlyDictionary<string, object> properties = ProductTelemetryHelpers.CreateFeatureProperties(
            "1.2.3",
            "windows",
            "desktop",
            "local",
            ciProvider: null,
            "prompt",
            "turn",
            success: true,
            metrics,
            attachmentCount: 0,
            exception: null);

        properties["provider_name"].Should().Be("openai chatgpt plus/pro");
        properties["model_id"].Should().Be("gpt-5-mini");
        properties["cached_input_tokens"].Should().Be(30);
    }

    [Fact]
    public void CreateAppStoppedProperties_ShouldBucketUsageTime()
    {
        IReadOnlyDictionary<string, object> properties = ProductTelemetryHelpers.CreateAppStoppedProperties(
            "1.2.3",
            "macos",
            "desktop",
            "local",
            ciProvider: null,
            TimeSpan.FromMinutes(7));

        properties["usage_time_bucket"].Should().Be("5m_to_15m");
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "true", "github_actions", "github_actions", "ci")]
    [InlineData("GITLAB_CI", "true", "gitlab_ci", "gitlab_ci", "ci")]
    [InlineData("BITBUCKET_BUILD_NUMBER", "42", "bitbucket_pipelines", "bitbucket_pipelines", "ci")]
    [InlineData("CI", "true", "ci", "generic_ci", "ci")]
    public void ResolveTelemetryAppSurface_ShouldDetectCiProviders(
        string variableName,
        string variableValue,
        string expectedSurface,
        string expectedProvider,
        string expectedExecutionEnvironment)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            [variableName] = variableValue
        };

        string appSurface = ProductTelemetryHelpers.ResolveTelemetryAppSurface(
            "cli",
            environment.GetValueOrDefault);
        string? ciProvider = ProductTelemetryHelpers.DetectCiProvider(environment.GetValueOrDefault);
        string executionEnvironment = ProductTelemetryHelpers.ResolveExecutionEnvironment(appSurface);

        appSurface.Should().Be(expectedSurface);
        ciProvider.Should().Be(expectedProvider);
        executionEnvironment.Should().Be(expectedExecutionEnvironment);
    }
}
