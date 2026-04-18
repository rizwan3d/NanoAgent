using FinalAgent.Application.Abstractions;
using FinalAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FinalAgent.Infrastructure.Storage;

internal sealed class UserDataPathProvider : IUserDataPathProvider
{
    private const string ConfigurationFileName = "agent-profile.json";

    private readonly ApplicationOptions _options;

    public UserDataPathProvider(IOptions<ApplicationOptions> options)
    {
        _options = options.Value;
    }

    public string GetConfigurationFilePath()
    {
        string root = ResolveFolder(
            Environment.SpecialFolder.ApplicationData,
            ".config");

        return Path.Combine(root, _options.StorageDirectoryName, ConfigurationFileName);
    }

    private static string ResolveFolder(Environment.SpecialFolder specialFolder, string fallbackRelativePath)
    {
        string folderPath = Environment.GetFolderPath(specialFolder);
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            return folderPath;
        }

        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            throw new InvalidOperationException($"Unable to resolve storage path for '{specialFolder}'.");
        }

        return Path.Combine(userProfilePath, fallbackRelativePath);
    }
}
