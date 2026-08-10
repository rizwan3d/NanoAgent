using FluentAssertions;
using StemCode.Infrastructure.Storage;

namespace StemCode.Tests.Infrastructure.Storage;

public sealed class UserDataPathProviderTests
{
    [Fact]
    public void GetLogsDirectoryPath_Should_ReturnStorageDirectoryLogsPath()
    {
        UserDataPathProvider sut = new();

        string logsDirectoryPath = sut.GetLogsDirectoryPath();

        Path.GetFileName(logsDirectoryPath).Should().Be("logs");
        logsDirectoryPath.Should().Contain("StemCode");
    }

    [Fact]
    public void GetMcpConfigurationFilePath_Should_ReturnAgentProfileJsonPath()
    {
        UserDataPathProvider sut = new();

        string mcpConfigurationFilePath = sut.GetMcpConfigurationFilePath();

        Path.GetFileName(mcpConfigurationFilePath).Should().Be("agent-profile.json");
        mcpConfigurationFilePath.Should().Contain("StemCode");
        mcpConfigurationFilePath.Should().Be(sut.GetConfigurationFilePath());
    }

    [Fact]
    public void GetSessionsDirectoryPath_Should_ReturnStorageDirectorySectionsPath()
    {
        UserDataPathProvider sut = new();

        string sectionsDirectoryPath = sut.GetSessionsDirectoryPath();

        Path.GetFileName(sectionsDirectoryPath).Should().Be("sessions");
        sectionsDirectoryPath.Should().Contain("StemCode");
    }
}
