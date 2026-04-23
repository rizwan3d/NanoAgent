using NanoAgent.Application.DependencyInjection;
using NanoAgent.Presentation.DependencyInjection;
using NanoAgent.Presentation.Cli.DependencyInjection;
using NanoAgent.Presentation.Cli.Hosting;
using NanoAgent.Presentation.Cli.Logging;
using NanoAgent.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NanoAgent;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using IHost host = CreateHost(args);

        ProcessExitCodeTracker exitCodeTracker = host.Services.GetRequiredService<ProcessExitCodeTracker>();
        ILogger startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NanoAgent.Startup");

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

        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: false);

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Services.Configure<ConsoleLifetimeOptions>(static options =>
        {
            options.SuppressStatusMessages = true;
        });

        builder.Services
            .AddApplication()
            .AddPresentation()
            .AddInfrastructure(builder.Configuration)
            .AddCliPresentation();

        return builder.Build();
    }
}
