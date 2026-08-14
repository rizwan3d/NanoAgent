namespace StemCode.Application.Abstractions;

public interface IUserDataPathProvider
{
    string GetConfigurationFilePath();

    string GetMcpConfigurationFilePath();

    string GetLogsDirectoryPath();

    string GetSessionsDirectoryPath();
}
