using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class JsonSessionStore : ISessionStore
{
    private readonly IUserDataPathProvider _pathProvider;

    public JsonSessionStore(IUserDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public async Task<SessionRecord?> LoadAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        string filePath = GetSessionFilePath(sessionId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json,
                SessionStorageJsonContext.Default.SessionRecord);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        SessionRecord session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        string filePath = GetSessionFilePath(session.SessionId);
        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Session path does not contain a parent directory.");

        FilePermissionHelper.EnsurePrivateDirectory(directoryPath);

        await using FileStream stream = new(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await JsonSerializer.SerializeAsync(
            stream,
            session,
            SessionStorageJsonContext.Default.SessionRecord,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);
        FilePermissionHelper.EnsurePrivateFile(filePath);
    }

    private string GetSessionFilePath(string sessionId)
    {
        string normalizedSessionId = NormalizeSessionId(sessionId);

        string sessionsDir = Path.Combine(
            _pathProvider.GetSectionsDirectoryPath(),
            "..",
            "sessions");

        return Path.Combine(
            Path.GetFullPath(sessionsDir),
            $"{normalizedSessionId}.json");
    }

    private static string NormalizeSessionId(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!Guid.TryParse(sessionId.Trim(), out Guid parsedSessionId))
        {
            throw new ArgumentException(
                "Session id must be a valid GUID.",
                nameof(sessionId));
        }

        return parsedSessionId.ToString("D");
    }
}
