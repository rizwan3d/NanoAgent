using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoAgent.Application.DependencyInjection;
using NanoAgent.Infrastructure.DependencyInjection;
using NanoAgent.Presentation.Cli.DependencyInjection;
using NanoAgent.Presentation.Cli.Hosting;
using NanoAgent.Presentation.Cli.Logging;
using NanoAgent.Presentation.DependencyInjection;

namespace NanoAgent.Hosting;

public static class NanoAgentHostBootstrap
{
    public static async Task<int> RunAsync(string[] args)
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

    public static IHost CreateHost(string[] args)
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
