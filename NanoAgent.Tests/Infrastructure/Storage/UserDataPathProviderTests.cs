using FluentAssertions;
using NanoAgent.Infrastructure.Storage;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class UserDataPathProviderTests
{
    [Fact]
    public void GetLogsDirectoryPath_Should_ReturnStorageDirectoryLogsPath()
    {
        UserDataPathProvider sut = new();

        string logsDirectoryPath = sut.GetLogsDirectoryPath();

        Path.GetFileName(logsDirectoryPath).Should().Be("logs");
        logsDirectoryPath.Should().Contain("NanoAgent");
    }

    [Fact]
    public void GetMcpConfigurationFilePath_Should_ReturnAgentProfileJsonPath()
    {
        UserDataPathProvider sut = new();

        string mcpConfigurationFilePath = sut.GetMcpConfigurationFilePath();

        Path.GetFileName(mcpConfigurationFilePath).Should().Be("agent-profile.json");
        mcpConfigurationFilePath.Should().Contain("NanoAgent");
        mcpConfigurationFilePath.Should().Be(sut.GetConfigurationFilePath());
    }

    [Fact]
    public void GetSectionsDirectoryPath_Should_ReturnStorageDirectorySectionsPath()
    {
        UserDataPathProvider sut = new();

        string sectionsDirectoryPath = sut.GetSectionsDirectoryPath();

        Path.GetFileName(sectionsDirectoryPath).Should().Be("sections");
        sectionsDirectoryPath.Should().Contain("NanoAgent");
    }
}
