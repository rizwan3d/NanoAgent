namespace NanoAgent.Application.Abstractions;

public interface IUserDataPathProvider
{
    string GetConfigurationFilePath();

    string GetLogsDirectoryPath();

    string GetSectionsDirectoryPath();
}
