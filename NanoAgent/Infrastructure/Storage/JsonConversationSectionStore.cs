using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Storage;

internal sealed class JsonConversationSectionStore : IConversationSectionStore
{
    private readonly IUserDataPathProvider _pathProvider;

    public JsonConversationSectionStore(IUserDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public async Task<ConversationSectionSnapshot?> LoadAsync(
        string sectionId,
        CancellationToken cancellationToken)
    {
        string filePath = GetSectionFilePath(sectionId);
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
                ConversationSectionStorageJsonContext.Default.ConversationSectionSnapshot);
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
        ConversationSectionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string filePath = GetSectionFilePath(snapshot.SectionId);
        string directoryPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Section path does not contain a parent directory.");

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
            snapshot,
            ConversationSectionStorageJsonContext.Default.ConversationSectionSnapshot,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);
        FilePermissionHelper.EnsurePrivateFile(filePath);
    }

    private string GetSectionFilePath(string sectionId)
    {
        string normalizedSectionId = NormalizeSectionId(sectionId);
        return Path.Combine(
            _pathProvider.GetSectionsDirectoryPath(),
            $"{normalizedSectionId}.json");
    }

    private static string NormalizeSectionId(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        if (!Guid.TryParse(sectionId.Trim(), out Guid parsedSectionId))
        {
            throw new ArgumentException(
                "Section id must be a valid GUID.",
                nameof(sectionId));
        }

        return parsedSectionId.ToString("D");
    }
}
