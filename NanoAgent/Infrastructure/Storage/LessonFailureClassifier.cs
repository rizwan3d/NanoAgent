using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class LessonFailureClassifier : ILessonFailureClassifier
{
    private const int MaxFieldCharacters = 1_000;
    private const int MaxClassifierSeconds = 20;
    private const string SystemPrompt =
        """
        You convert failed coding-agent tool attempts into concrete, reusable lesson memory.
        Return only a JSON object with this shape:
        {
          "trigger": "short symptom or error code that should retrieve this later",
          "problem": "specific root-cause hypothesis or mistake pattern",
          "lesson": "actionable future behavior that prevents or fixes this class of failure",
          "tags": ["short", "lowercase", "retrieval", "tokens"]
        }

        Rules:
        - Make the lesson reusable, not a narration of this one attempt.
        - Prefer command syntax, working-directory, project targeting, package/dependency, source-code, or tool-argument root causes.
        - Do not write generic advice such as "be careful", "check arguments", or "do not repeat the failed pattern".
        - Do not include secrets, credentials, private paths, or long raw logs.
        - If there is no reusable lesson, return {}.
        """;

    private readonly IApiKeySecretStore _secretStore;
    private readonly IConversationProviderClient _providerClient;
    private readonly IConversationResponseMapper _responseMapper;
    private readonly IConversationConfigurationAccessor _configurationAccessor;

    public LessonFailureClassifier(
        IApiKeySecretStore secretStore,
        IConversationProviderClient providerClient,
        IConversationResponseMapper responseMapper,
        IConversationConfigurationAccessor configurationAccessor)
    {
        _secretStore = secretStore;
        _providerClient = providerClient;
        _responseMapper = responseMapper;
        _configurationAccessor = configurationAccessor;
    }

    public async Task<LessonFailureClassification?> ClassifyAsync(
        ReplSessionContext session,
        LessonFailureClassificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string? apiKey = await LoadProviderSecretAsync(session, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        ConversationSettings settings = _configurationAccessor.GetSettings();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(GetClassifierTimeout(settings.RequestTimeout));

        ConversationProviderPayload payload = await _providerClient.SendAsync(
            new ConversationProviderRequest(
                session.ProviderProfile,
                apiKey,
                session.ActiveModelId,
                [ConversationRequestMessage.User(BuildUserPrompt(request))],
                SystemPrompt,
                AvailableTools: [],
                session.ReasoningEffort),
            timeoutSource.Token);

        ConversationResponse response = _responseMapper.Map(payload);
        return TryParseClassification(response.AssistantMessage, out LessonFailureClassification? classification)
            ? classification
            : null;
    }

    private async Task<string?> LoadProviderSecretAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.ActiveProviderName))
        {
            string? providerSecret = await _secretStore.LoadAsync(
                session.ActiveProviderName,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(providerSecret))
            {
                return providerSecret;
            }
        }

        return await _secretStore.LoadAsync(cancellationToken);
    }

    private static TimeSpan GetClassifierTimeout(TimeSpan conversationTimeout)
    {
        TimeSpan cap = TimeSpan.FromSeconds(MaxClassifierSeconds);
        if (conversationTimeout <= TimeSpan.Zero)
        {
            return cap;
        }

        return conversationTimeout < cap
            ? conversationTimeout
            : cap;
    }

    private static string BuildUserPrompt(LessonFailureClassificationRequest request)
    {
        StringBuilder builder = new();
        builder.AppendLine("Classify this failed tool attempt into a reusable lesson.");
        AppendField(builder, "Tool", request.ToolName);
        AppendField(builder, "Trigger", request.Trigger);
        AppendField(builder, "Problem", request.Problem);
        AppendField(builder, "Failed attempt", request.AttemptSummary);
        AppendOptionalField(builder, "Command", request.Command);
        AppendOptionalField(builder, "Failure signature", request.FailureSignature);

        if (request.Tags.Count > 0)
        {
            AppendField(builder, "Existing tags", string.Join(", ", request.Tags));
        }

        return builder.ToString().Trim();
    }

    private static void AppendOptionalField(
        StringBuilder builder,
        string label,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AppendField(builder, label, value);
    }

    private static void AppendField(
        StringBuilder builder,
        string label,
        string value)
    {
        builder
            .Append(label)
            .Append(": ")
            .AppendLine(TrimField(value));
    }

    private static bool TryParseClassification(
        string? text,
        out LessonFailureClassification? classification)
    {
        classification = null;

        if (string.IsNullOrWhiteSpace(text) ||
            !TryExtractJsonObject(text, out string? json) ||
            json is null)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetNonEmptyString(root, "trigger", out string? trigger) ||
                !TryGetNonEmptyString(root, "problem", out string? problem) ||
                !TryGetNonEmptyString(root, "lesson", out string? lesson))
            {
                return false;
            }

            string[] tags = TryReadTags(root, "tags");
            classification = new LessonFailureClassification(
                TrimField(trigger!),
                TrimField(problem!),
                TrimField(lesson!),
                tags);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractJsonObject(
        string text,
        out string? json)
    {
        json = null;
        int start = text.IndexOf('{', StringComparison.Ordinal);
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        json = text[start..(end + 1)];
        return true;
    }

    private static bool TryGetNonEmptyString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!.Trim();
            return true;
        }

        value = null;
        return false;
    }

    private static string[] TryReadTags(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()?.Trim().ToLowerInvariant())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Cast<string>()
            .ToArray();
    }

    private static string TrimField(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();

        return normalized.Length <= MaxFieldCharacters
            ? normalized
            : normalized[..Math.Max(0, MaxFieldCharacters - 3)].TrimEnd() + "...";
    }
}
