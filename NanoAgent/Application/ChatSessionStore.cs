using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoAgent;

internal sealed class ChatSessionStore
{
    private readonly string _sessionsDirectoryPath;

    public ChatSessionStore(string sessionsDirectoryPath)
    {
        _sessionsDirectoryPath = sessionsDirectoryPath;
        Directory.CreateDirectory(_sessionsDirectoryPath);
    }

    public bool Exists(string sessionId) => File.Exists(GetSessionFilePath(sessionId));

    public ChatSessionRecord Load(string sessionId)
    {
        string filePath = GetSessionFilePath(sessionId);
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        string json = File.ReadAllText(filePath);

        try
        {
            ChatSessionRecord? record = JsonSerializer.Deserialize(json, ChatSessionJsonContext.Default.ChatSessionRecord);
            if (record is null)
            {
                throw new InvalidOperationException($"Session '{sessionId}' could not be loaded.");
            }

            return record;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Session '{sessionId}' contains invalid JSON.", exception);
        }
    }

    public void Save(ChatSessionRecord record)
    {
        Directory.CreateDirectory(_sessionsDirectoryPath);
        string filePath = GetSessionFilePath(record.SessionId);
        string json = JsonSerializer.Serialize(record, ChatSessionJsonContext.Default.ChatSessionRecord);
        File.WriteAllText(filePath, json + Environment.NewLine);
    }

    public IReadOnlyList<ChatSessionSummary> ListRecent(int maxCount)
    {
        Directory.CreateDirectory(_sessionsDirectoryPath);

        return Directory.EnumerateFiles(_sessionsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                try
                {
                    string json = File.ReadAllText(path);
                    ChatSessionRecord? record = JsonSerializer.Deserialize(json, ChatSessionJsonContext.Default.ChatSessionRecord);
                    if (record is null)
                    {
                        return null;
                    }

                    return CreateSummary(record);
                }
                catch
                {
                    return null;
                }
            })
            .Where(summary => summary is not null)
            .Cast<ChatSessionSummary>()
            .OrderByDescending(summary => summary.UpdatedAtUtc)
            .Take(Math.Max(0, maxCount))
            .ToArray();
    }

    public static string CreateSessionId() =>
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

    private string GetSessionFilePath(string sessionId) =>
        Path.Combine(_sessionsDirectoryPath, $"{sessionId}.json");

    private static ChatSessionSummary CreateSummary(ChatSessionRecord record)
    {
        ChatMessage? lastMessage = record.Messages
            .LastOrDefault(message => !string.Equals(message.Role, ChatRole.System, StringComparison.Ordinal));

        string preview = lastMessage?.Content?.Trim() ?? "<empty>";
        preview = preview.Replace("\r\n", " ").Replace('\n', ' ').Trim();
        if (preview.Length > 80)
        {
            preview = preview[..77] + "...";
        }

        return new ChatSessionSummary(
            record.SessionId,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            preview,
            record.Messages.Length);
    }
}

internal sealed class ChatSessionRecord
{
    public string SessionId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public ChatMessage[] Messages { get; init; } = Array.Empty<ChatMessage>();
}

internal sealed record ChatSessionSummary(
    string SessionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Preview,
    int MessageCount);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ChatSessionRecord))]
internal sealed partial class ChatSessionJsonContext : JsonSerializerContext
{
}
