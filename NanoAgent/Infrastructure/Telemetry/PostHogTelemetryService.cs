using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Infrastructure.Telemetry;

internal sealed class PostHogTelemetryService : IProductTelemetry, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Channel<TelemetryEnvelope>? _queue;
    private readonly string _appSurface;
    private readonly string? _captureEndpoint;
    private readonly string? _distinctId;
    private readonly bool _enabled;
    private readonly string _osFamily;
    private readonly string? _projectToken;
    private readonly Task? _senderTask;
    private readonly TimeProvider _timeProvider;
    private readonly string _version;
    private DateTimeOffset? _startedAtUtc;

    public PostHogTelemetryService(
        HttpClient httpClient,
        IUserDataPathProvider userDataPathProvider,
        IOptions<ApplicationOptions> options,
        BackendRuntimeOptions runtimeOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(userDataPathProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _version = ProductTelemetryHelpers.GetNanoAgentVersion();
        _osFamily = ProductTelemetryHelpers.GetOsFamily();
        _appSurface = runtimeOptions.AppSurface;

        TelemetryOptions telemetryOptions = options.Value.Telemetry ?? new TelemetryOptions();
        string? projectToken = string.IsNullOrWhiteSpace(telemetryOptions.ProjectToken)
            ? null
            : telemetryOptions.ProjectToken.Trim();
        string host = ProductTelemetryHelpers.NormalizeHost(telemetryOptions.Host);
        _captureEndpoint = Uri.TryCreate($"{host}/i/v0/e/", UriKind.Absolute, out Uri? captureUri)
            ? captureUri.ToString()
            : null;
        _projectToken = projectToken;
        _enabled =
            telemetryOptions.Enabled &&
            !string.IsNullOrWhiteSpace(_projectToken) &&
            !string.IsNullOrWhiteSpace(_captureEndpoint);

        if (!_enabled)
        {
            return;
        }

        _distinctId = LoadOrCreateDistinctId(userDataPathProvider);
        _queue = Channel.CreateUnbounded<TelemetryEnvelope>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        _senderTask = Task.Run(ProcessQueueAsync);
    }

    public void TrackAppStarted()
    {
        if (!_enabled)
        {
            return;
        }

        if (_startedAtUtc is not null)
        {
            return;
        }

        _startedAtUtc = _timeProvider.GetUtcNow();
        Enqueue(
            "nanoagent app started",
            ProductTelemetryHelpers.CreateAppStartedProperties(
                _version,
                _osFamily,
                _appSurface));
    }

    public void TrackAppStopped()
    {
        if (!_enabled || _startedAtUtc is not { } startedAtUtc)
        {
            return;
        }

        TimeSpan usageTime = _timeProvider.GetUtcNow() - startedAtUtc;
        Enqueue(
            "nanoagent app stopped",
            ProductTelemetryHelpers.CreateAppStoppedProperties(
                _version,
                _osFamily,
                _appSurface,
                usageTime));
        _startedAtUtc = null;
    }

    public void TrackFeatureUsed(
        string featureName,
        string interactionKind,
        bool success,
        ConversationTurnMetrics? metrics = null,
        int attachmentCount = 0,
        Exception? exception = null)
    {
        if (!_enabled)
        {
            return;
        }

        Enqueue(
            "nanoagent feature used",
            ProductTelemetryHelpers.CreateFeatureProperties(
                _version,
                _osFamily,
                _appSurface,
                featureName,
                interactionKind,
                success,
                metrics,
                attachmentCount,
                exception));
    }

    public async ValueTask DisposeAsync()
    {
        if (_queue is null)
        {
            return;
        }

        _queue.Writer.TryComplete();
        if (_senderTask is null)
        {
            return;
        }

        try
        {
            await _senderTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessQueueAsync()
    {
        if (_queue is null)
        {
            return;
        }

        await foreach (TelemetryEnvelope envelope in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await SendAsync(envelope);
            }
            catch
            {
                // Telemetry must never affect product behavior.
            }
        }
    }

    private Task SendAsync(TelemetryEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(_captureEndpoint) ||
            string.IsNullOrWhiteSpace(_projectToken) ||
            string.IsNullOrWhiteSpace(_distinctId))
        {
            return Task.CompletedTask;
        }

        Dictionary<string, object?> properties = new(StringComparer.Ordinal);
        foreach ((string key, object value) in envelope.Properties)
        {
            properties[key] = value;
        }

        properties["$process_person_profile"] = false;

        StringContent content = new(
            CreatePayloadJson(
                _projectToken,
                envelope.EventName,
                _distinctId,
                properties),
            Encoding.UTF8,
            "application/json");

        return _httpClient.PostAsync(_captureEndpoint, content);
    }

    private void Enqueue(
        string eventName,
        IReadOnlyDictionary<string, object> properties)
    {
        if (_queue is null)
        {
            return;
        }

        _queue.Writer.TryWrite(new TelemetryEnvelope(eventName, properties));
    }

    private static string LoadOrCreateDistinctId(IUserDataPathProvider userDataPathProvider)
    {
        string configurationPath = userDataPathProvider.GetConfigurationFilePath();
        string applicationDirectory = Path.GetDirectoryName(configurationPath)
            ?? AppContext.BaseDirectory;
        string distinctIdPath = Path.Combine(applicationDirectory, "telemetry-id.txt");

        try
        {
            if (File.Exists(distinctIdPath))
            {
                string existing = File.ReadAllText(distinctIdPath).Trim();
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }
            }

            Directory.CreateDirectory(applicationDirectory);
            string distinctId = Guid.NewGuid().ToString("N");
            File.WriteAllText(distinctIdPath, distinctId, Encoding.UTF8);
            return distinctId;
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static string CreatePayloadJson(
        string projectToken,
        string eventName,
        string distinctId,
        IReadOnlyDictionary<string, object?> properties)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("api_key", projectToken);
            writer.WriteString("event", eventName);
            writer.WriteString("distinct_id", distinctId);
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            foreach ((string key, object? value) in properties)
            {
                if (value is null)
                {
                    continue;
                }

                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteValue(
        Utf8JsonWriter writer,
        object value)
    {
        switch (value)
        {
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                return;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                return;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                return;
            case int intValue:
                writer.WriteNumberValue(intValue);
                return;
            case long longValue:
                writer.WriteNumberValue(longValue);
                return;
            default:
                writer.WriteStringValue(value.ToString());
                return;
        }
    }

    private sealed record TelemetryEnvelope(
        string EventName,
        IReadOnlyDictionary<string, object> Properties);
}
