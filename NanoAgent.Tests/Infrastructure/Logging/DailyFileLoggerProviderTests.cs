using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Logging;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Tests.Infrastructure.Logging;

public sealed class DailyFileLoggerProviderTests : IDisposable
{
    private readonly string _tempRoot;

    public DailyFileLoggerProviderTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Logs-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void CreateLogger_Should_WriteEntriesToDailyLogFile()
    {
        FixedTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero));
        DailyFileLoggerProvider sut = new(
            new StubUserDataPathProvider(Path.Combine(_tempRoot, "logs")),
            new StubHostEnvironment("NanoAgent"),
            timeProvider);

        ILogger logger = sut.CreateLogger("NanoAgent.Tests.Logging");
        logger.LogInformation("Host startup sequence has begun.");

        string logFilePath = Path.Combine(_tempRoot, "logs", "2026-04-20.log");

        File.Exists(logFilePath).Should().BeTrue();
        string contents = File.ReadAllText(logFilePath);
        contents.Should().Contain("info: NanoAgent.Tests.Logging Host startup sequence has begun.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string applicationName)
        {
            ApplicationName = applicationName;
            ContentRootFileProvider = new NullFileProvider();
            ContentRootPath = AppContext.BaseDirectory;
            EnvironmentName = Environments.Production;
        }

        public string ApplicationName { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }

        public string ContentRootPath { get; set; }

        public string EnvironmentName { get; set; }
    }

    private sealed class StubUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _logsDirectoryPath;

        public StubUserDataPathProvider(string logsDirectoryPath)
        {
            _logsDirectoryPath = logsDirectoryPath;
        }

        public string GetConfigurationFilePath()
        {
            return Path.Combine(_logsDirectoryPath, "..", "agent-profile.json");
        }

        public string GetLogsDirectoryPath()
        {
            return _logsDirectoryPath;
        }

        public string GetSectionsDirectoryPath()
        {
            return Path.Combine(_logsDirectoryPath, "..", "sections");
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
