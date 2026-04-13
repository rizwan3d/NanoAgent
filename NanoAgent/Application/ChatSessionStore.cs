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

    public static string CreateSessionId() =>
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

    private string GetSessionFilePath(string sessionId) =>
        Path.Combine(_sessionsDirectoryPath, $"{sessionId}.json");
}

internal sealed class ChatSessionRecord
{
    public string SessionId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public ChatMessage[] Messages { get; init; } = Array.Empty<ChatMessage>();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ChatSessionRecord))]
internal sealed partial class ChatSessionJsonContext : JsonSerializerContext
{
}
