using FluentAssertions;
using Microsoft.Extensions.Options;
using StemCode.Application.Abstractions;
using StemCode.Application.Backend;
using StemCode.Application.Models;
using StemCode.Infrastructure.Configuration;
using StemCode.Infrastructure.Telemetry;
using System.Net;
using System.Text.Json;

namespace StemCode.Tests.Infrastructure.Telemetry;

public sealed class PostHogTelemetryServiceTests
{
    [Fact]
    public async Task TrackAppStartedAndFeatureUsed_Should_SendIdentifyAndReuseSessionId()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.DesktopSurface);

        ConversationTurnMetrics metrics = new(
            TimeSpan.FromSeconds(8),
            estimatedOutputTokens: 220,
            estimatedInputTokens: 140,
            cachedInputTokens: 25,
            providerName: "OpenAI",
            modelId: "gpt-5");

        sut.TrackAppStarted();
        sut.TrackFeatureUsed("apply_patch", "tool", success: true, metrics);
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(3);

        using JsonDocument identifyRequest = ParseBody(handler.Requests[0]);
        using JsonDocument appStartedRequest = ParseBody(handler.Requests[1]);
        using JsonDocument featureRequest = ParseBody(handler.Requests[2]);

        identifyRequest.RootElement.GetProperty("event").GetString().Should().Be("$identify");
        appStartedRequest.RootElement.GetProperty("event").GetString().Should().Be("app started");
        featureRequest.RootElement.GetProperty("event").GetString().Should().Be("feature used");

        string identifySessionId = identifyRequest.RootElement
            .GetProperty("properties")
            .GetProperty("$session_id")
            .GetString()!;
        string appStartedSessionId = appStartedRequest.RootElement
            .GetProperty("properties")
            .GetProperty("$session_id")
            .GetString()!;
        string featureSessionId = featureRequest.RootElement
            .GetProperty("properties")
            .GetProperty("$session_id")
            .GetString()!;

        identifySessionId.Should().Be(appStartedSessionId);
        identifySessionId.Should().Be(featureSessionId);
        identifySessionId[14].Should().Be('7');

        JsonElement personProperties = identifyRequest.RootElement
            .GetProperty("properties")
            .GetProperty("$set");
        personProperties.GetProperty("app_surface").GetString().Should().Be("desktop");
        personProperties.GetProperty("execution_environment").GetString().Should().Be("local");
        personProperties.GetProperty("is_ci").GetBoolean().Should().BeFalse();
        personProperties.GetProperty("os_family").GetString().Should().Be(ProductTelemetryHelpers.GetOsFamily());
        personProperties.GetProperty("app_version").GetString().Should().NotBeNullOrWhiteSpace();

        JsonElement appStartedProperties = appStartedRequest.RootElement.GetProperty("properties");
        appStartedProperties.GetProperty("app_surface").GetString().Should().Be("desktop");
        appStartedProperties.TryGetProperty("$process_person_profile", out _).Should().BeFalse();

        JsonElement featureProperties = featureRequest.RootElement.GetProperty("properties");
        featureProperties.GetProperty("feature_name").GetString().Should().Be("apply_patch");
        featureProperties.GetProperty("interaction_kind").GetString().Should().Be("tool");
        featureProperties.GetProperty("success").GetBoolean().Should().BeTrue();
        featureProperties.GetProperty("provider_name").GetString().Should().Be("openai");
        featureProperties.GetProperty("model_id").GetString().Should().Be("gpt-5");
        featureProperties.GetProperty("input_tokens").GetInt32().Should().Be(140);
        featureProperties.GetProperty("output_tokens").GetInt32().Should().Be(220);
        featureProperties.GetProperty("cached_input_tokens").GetInt32().Should().Be(25);
        featureProperties.TryGetProperty("$process_person_profile", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TrackFeatureUsed_Should_SendIdentifyBeforeFeatureEvent_WhenAppStartWasNotTracked()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.VsCodeSurface);

        sut.TrackFeatureUsed("session", "command", success: false);
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(2);

        using JsonDocument identifyRequest = ParseBody(handler.Requests[0]);
        using JsonDocument featureRequest = ParseBody(handler.Requests[1]);

        identifyRequest.RootElement.GetProperty("event").GetString().Should().Be("$identify");
        featureRequest.RootElement.GetProperty("event").GetString().Should().Be("feature used");
        identifyRequest.RootElement
            .GetProperty("properties")
            .GetProperty("$set")
            .GetProperty("app_surface")
            .GetString()
            .Should()
            .Be("vscode");
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "true", "github_actions", "github_actions")]
    [InlineData("GITLAB_CI", "true", "gitlab_ci", "gitlab_ci")]
    [InlineData("BITBUCKET_BUILD_NUMBER", "123", "bitbucket_pipelines", "bitbucket_pipelines")]
    public async Task TrackAppStarted_ShouldAnnotateCiSurfaceWhenRunningInKnownCi(
        string variableName,
        string variableValue,
        string expectedSurface,
        string expectedProvider)
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            [variableName] = variableValue
        };

        PostHogTelemetryService sut = CreateSut(
            handler,
            BackendRuntimeOptions.CliSurface,
            environment.GetValueOrDefault);

        sut.TrackAppStarted();
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(2);

        using JsonDocument identifyRequest = ParseBody(handler.Requests[0]);
        using JsonDocument appStartedRequest = ParseBody(handler.Requests[1]);

        JsonElement identifyProperties = identifyRequest.RootElement
            .GetProperty("properties")
            .GetProperty("$set");
        identifyProperties.GetProperty("app_surface").GetString().Should().Be(expectedSurface);
        identifyProperties.GetProperty("execution_environment").GetString().Should().Be("ci");
        identifyProperties.GetProperty("is_ci").GetBoolean().Should().BeTrue();
        identifyProperties.GetProperty("ci_provider").GetString().Should().Be(expectedProvider);

        JsonElement appStartedProperties = appStartedRequest.RootElement.GetProperty("properties");
        appStartedProperties.GetProperty("app_surface").GetString().Should().Be(expectedSurface);
        appStartedProperties.GetProperty("execution_environment").GetString().Should().Be("ci");
        appStartedProperties.GetProperty("is_ci").GetBoolean().Should().BeTrue();
        appStartedProperties.GetProperty("ci_provider").GetString().Should().Be(expectedProvider);
    }

    [Fact]
    public async Task TrackToolInvoked_Should_SendIdentifyAndToolEvent_OnSuccess()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.CliSurface);

        sut.TrackToolInvoked(
            "file_read",
            ToolResultStatus.Success,
            success: true,
            TimeSpan.FromMilliseconds(420),
            ConversationExecutionPhase.Execution,
            modelId: "gpt-5-mini",
            providerName: "OpenAI",
            errorMessage: null);
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(2);

        using JsonDocument identifyRequest = ParseBody(handler.Requests[0]);
        using JsonDocument toolRequest = ParseBody(handler.Requests[1]);

        identifyRequest.RootElement.GetProperty("event").GetString().Should().Be("$identify");
        toolRequest.RootElement.GetProperty("event").GetString().Should().Be("tool invoked");

        JsonElement toolProperties = toolRequest.RootElement.GetProperty("properties");
        toolProperties.GetProperty("tool_name").GetString().Should().Be("file_read");
        toolProperties.GetProperty("tool_status").GetString().Should().Be("success");
        toolProperties.GetProperty("success").GetBoolean().Should().BeTrue();
        toolProperties.GetProperty("duration_ms").GetInt64().Should().Be(420);
        toolProperties.GetProperty("execution_phase").GetString().Should().Be("execution");
        toolProperties.GetProperty("model_id").GetString().Should().Be("gpt-5-mini");
        toolProperties.GetProperty("provider_name").GetString().Should().Be("openai");
        toolProperties.GetProperty("tool_type").GetString().Should().Be("file");
        toolProperties.TryGetProperty("error_message", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TrackToolInvoked_Should_IncludeErrorMessage_OnFailure()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.DesktopSurface);

        sut.TrackToolInvoked(
            "ShellCommand",
            ToolResultStatus.ExecutionError,
            success: false,
            TimeSpan.FromMilliseconds(1500),
            ConversationExecutionPhase.Execution,
            modelId: "gpt-5",
            providerName: "OpenAI",
            errorMessage: "Tool execution failed unexpectedly: exit code 1");
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(2);

        using JsonDocument toolRequest = ParseBody(handler.Requests[1]);

        JsonElement toolProperties = toolRequest.RootElement.GetProperty("properties");
        toolProperties.GetProperty("tool_name").GetString().Should().Be("shellcommand");
        toolProperties.GetProperty("tool_status").GetString().Should().Be("execution_error");
        toolProperties.GetProperty("success").GetBoolean().Should().BeFalse();
        toolProperties.GetProperty("error_message").GetString().Should()
            .Be("Tool execution failed unexpectedly: exit code 1");
        toolProperties.GetProperty("tool_type").GetString().Should().Be("shell");
        toolProperties.GetProperty("$session_id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TrackProviderRequest_Should_SendIdentifyAndProviderEvent_OnStreamedSuccess()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.CliSurface);

        sut.TrackProviderRequest(
            "OpenAiCompatible",
            success: true,
            TimeSpan.FromMilliseconds(8000),
            streamed: true,
            TimeSpan.FromMilliseconds(6000),
            retryCount: 0,
            errorMessage: null);
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(2);

        using JsonDocument identifyRequest = ParseBody(handler.Requests[0]);
        using JsonDocument providerRequest = ParseBody(handler.Requests[1]);

        identifyRequest.RootElement.GetProperty("event").GetString().Should().Be("$identify");
        providerRequest.RootElement.GetProperty("event").GetString().Should().Be("provider request");

        JsonElement props = providerRequest.RootElement.GetProperty("properties");
        props.GetProperty("provider_name").GetString().Should().Be("open_ai_compatible");
        props.GetProperty("success").GetBoolean().Should().BeTrue();
        props.GetProperty("latency_ms").GetInt64().Should().Be(8000);
        props.GetProperty("latency_bucket").GetString().Should().Be("5s_to_15s");
        props.GetProperty("streamed").GetBoolean().Should().BeTrue();
        props.GetProperty("stream_latency_ms").GetInt64().Should().Be(6000);
        props.GetProperty("stream_latency_bucket").GetString().Should().Be("5s_to_15s");
        props.GetProperty("retry_count_bucket").GetString().Should().Be("0");
        props.TryGetProperty("error_message", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TrackProviderRequest_Should_IncludeErrorMessage_OnFailure()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.DesktopSurface);

        sut.TrackProviderRequest(
            "Anthropic",
            success: false,
            TimeSpan.FromSeconds(30),
            streamed: false,
            TimeSpan.Zero,
            retryCount: 3,
            errorMessage: "Provider returned HTTP 429: rate limited");
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(2);

        using JsonDocument providerRequest = ParseBody(handler.Requests[1]);

        JsonElement props = providerRequest.RootElement.GetProperty("properties");
        props.GetProperty("provider_name").GetString().Should().Be("anthropic");
        props.GetProperty("success").GetBoolean().Should().BeFalse();
        props.GetProperty("streamed").GetBoolean().Should().BeFalse();
        props.GetProperty("latency_ms").GetInt64().Should().Be(30000);
        props.GetProperty("latency_bucket").GetString().Should().Be("ge_60s");
        props.GetProperty("retry_count_bucket").GetString().Should().Be("ge_6");
        props.GetProperty("error_message").GetString().Should()
            .Be("Provider returned HTTP 429: rate limited");
    }

    private static PostHogTelemetryService CreateSut(
        HttpMessageHandler handler,
        string appSurface,
        Func<string, string?>? environmentVariableReader = null)
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "stemcode-posthog-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        ApplicationOptions applicationOptions = new()
        {
            Telemetry = new TelemetryOptions
            {
                Enabled = true,
                Host = TelemetryOptions.DefaultHost,
                ProjectToken = "test-project-token"
            }
        };

        return new PostHogTelemetryService(
            new HttpClient(handler),
            new TestUserDataPathProvider(tempRoot),
            Options.Create(applicationOptions),
            new BackendRuntimeOptions(appSurface: appSurface),
            TimeProvider.System,
            environmentVariableReader);
    }

    private static JsonDocument ParseBody(RecordedRequest request)
    {
        request.Body.Should().NotBeNull();
        return JsonDocument.Parse(request.Body!);
    }

    private sealed class TestUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _rootPath;

        public TestUserDataPathProvider(string rootPath)
        {
            _rootPath = rootPath;
        }

        public string GetConfigurationFilePath() => Path.Combine(_rootPath, "config.json");

        public string GetMcpConfigurationFilePath() => Path.Combine(_rootPath, "mcp.json");

        public string GetLogsDirectoryPath() => Path.Combine(_rootPath, "logs");

        public string GetSessionsDirectoryPath() => Path.Combine(_rootPath, "sessions");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handle;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        {
            _handle = handle;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(body));
            return _handle(request);
        }
    }

    private sealed record RecordedRequest(string? Body);
}
