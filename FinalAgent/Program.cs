using FinalAgent.Application.DependencyInjection;
using FinalAgent.ConsoleHost.DependencyInjection;
using FinalAgent.ConsoleHost.Hosting;
using FinalAgent.ConsoleHost.Logging;
using FinalAgent.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinalAgent;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using IHost host = CreateHost(args);

        ProcessExitCodeTracker exitCodeTracker = host.Services.GetRequiredService<ProcessExitCodeTracker>();
        ILogger startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FinalAgent.Startup");

        try
        {
            await host.RunAsync();
            return exitCodeTracker.ExitCode;
        }
        catch (OptionsValidationException exception)
        {
            HostLogMessages.ConfigurationValidationFailed(startupLogger, exception.Message);
            return ExitCodes.ConfigurationError;
        }
        catch (Exception exception)
        {
            HostLogMessages.UnhandledHostFailure(startupLogger, exception);
            return ExitCodes.UnhandledException;
        }
    }

    private static IHost CreateHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.ConfigureConsoleHost();

        builder.Services
            .AddApplication()
            .AddInfrastructure(builder.Configuration)
            .AddConsoleHost();

        return builder.Build();
    }
}
