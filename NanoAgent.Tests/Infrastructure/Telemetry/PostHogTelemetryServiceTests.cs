using FluentAssertions;
using Microsoft.Extensions.Options;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Infrastructure.Telemetry;
using System.Net;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Telemetry;

public sealed class PostHogTelemetryServiceTests
{
    [Fact]
    public async Task TrackAppStartedAndFeatureUsed_Should_SendIdentifyAndReuseSessionId()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        PostHogTelemetryService sut = CreateSut(handler, BackendRuntimeOptions.DesktopSurface);

        sut.TrackAppStarted();
        sut.TrackFeatureUsed("apply_patch", "tool", success: true);
        await sut.DisposeAsync();

        handler.Requests.Should().HaveCount(3);

        using JsonDocument identifyRequest = ParseBody(handler.Requests[0]);
        using JsonDocument appStartedRequest = ParseBody(handler.Requests[1]);
        using JsonDocument featureRequest = ParseBody(handler.Requests[2]);

        identifyRequest.RootElement.GetProperty("event").GetString().Should().Be("$identify");
        appStartedRequest.RootElement.GetProperty("event").GetString().Should().Be("nanoagent app started");
        featureRequest.RootElement.GetProperty("event").GetString().Should().Be("nanoagent feature used");

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
        personProperties.GetProperty("nanoagent_version").GetString().Should().NotBeNullOrWhiteSpace();

        JsonElement appStartedProperties = appStartedRequest.RootElement.GetProperty("properties");
        appStartedProperties.GetProperty("app_surface").GetString().Should().Be("desktop");
        appStartedProperties.TryGetProperty("$process_person_profile", out _).Should().BeFalse();

        JsonElement featureProperties = featureRequest.RootElement.GetProperty("properties");
        featureProperties.GetProperty("feature_name").GetString().Should().Be("apply_patch");
        featureProperties.GetProperty("interaction_kind").GetString().Should().Be("tool");
        featureProperties.GetProperty("success").GetBoolean().Should().BeTrue();
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
        featureRequest.RootElement.GetProperty("event").GetString().Should().Be("nanoagent feature used");
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

    private static PostHogTelemetryService CreateSut(
        HttpMessageHandler handler,
        string appSurface,
        Func<string, string?>? environmentVariableReader = null)
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-posthog-tests",
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
