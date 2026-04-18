using FinalAgent.Application.Abstractions;
using FinalAgent.ConsoleHost.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinalAgent.ConsoleHost.Hosting;

internal sealed class ConsoleApplicationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly ProcessExitCodeTracker _exitCodeTracker;
    private readonly ILogger<ConsoleApplicationHostedService> _logger;

    public ConsoleApplicationHostedService(
        IServiceScopeFactory serviceScopeFactory,
        IHostApplicationLifetime hostApplicationLifetime,
        ProcessExitCodeTracker exitCodeTracker,
        ILogger<ConsoleApplicationHostedService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _hostApplicationLifetime = hostApplicationLifetime;
        _exitCodeTracker = exitCodeTracker;
        _logger = logger;

        _hostApplicationLifetime.ApplicationStopping.Register(() =>
            HostLogMessages.ApplicationStopping(_logger));

        _hostApplicationLifetime.ApplicationStopped.Register(() =>
            HostLogMessages.ApplicationStopped(_logger, _exitCodeTracker.ExitCode));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HostLogMessages.ApplicationStarting(_logger);

        try
        {
            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            IApplicationRunner applicationRunner = scope.ServiceProvider.GetRequiredService<IApplicationRunner>();

            await applicationRunner.RunAsync(stoppingToken);

            _exitCodeTracker.Set(ExitCodes.Success);
            HostLogMessages.RunCompleted(_logger, _exitCodeTracker.ExitCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _exitCodeTracker.Set(ExitCodes.Cancelled);
            HostLogMessages.RunCancelled(_logger);
        }
        catch (Exception exception)
        {
            _exitCodeTracker.Set(ExitCodes.UnhandledException);
            HostLogMessages.RunFailed(_logger, exception);
        }
        finally
        {
            _hostApplicationLifetime.StopApplication();
        }
    }
}
