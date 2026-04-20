using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Infrastructure.Conversation;
using NanoAgent.Infrastructure.Logging;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Models;
using NanoAgent.Infrastructure.Storage;
using NanoAgent.Infrastructure.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NanoAgent.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();
        services.AddSingleton<IConversationSectionStore, JsonConversationSectionStore>();
        services.AddSingleton<IWorkspaceRootProvider, CurrentDirectoryWorkspaceRootProvider>();
        services.AddSingleton<IWorkspaceFileService, WorkspaceFileService>();
        services.AddSingleton<IShellCommandService, ShellCommandService>();
        services.AddSingleton<IPermissionConfigurationAccessor, PermissionConfigurationAccessor>();
        services.AddHttpClient<IWebSearchService, DuckDuckGoWebSearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddSingleton<IAgentConfigurationStore, JsonAgentConfigurationStore>();
        services.AddSingleton<IApiKeySecretStore, ApiKeySecretStore>();
        services.AddSingleton<IModelCache, InMemoryModelCache>();
        services.AddSingleton<IConversationConfigurationAccessor, ConversationConfigurationAccessor>();
        services.AddSingleton<IConversationResponseMapper, OpenAiConversationResponseMapper>();
        services.AddSingleton<IModelSelectionConfigurationAccessor, ModelSelectionConfigurationAccessor>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPlatformCredentialStore>(CreatePlatformCredentialStore());
        services.AddSingleton<ILoggerProvider, DailyFileLoggerProvider>();
        services.AddSingleton<IValidateOptions<ApplicationOptions>, ApplicationOptionsValidator>();
        services.AddHttpClient<IConversationProviderClient, OpenAiCompatibleConversationProviderClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient<IModelProviderClient, OpenAiCompatibleModelProviderClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services
            .AddOptions<ApplicationOptions>()
            .BindConfiguration(ApplicationOptions.SectionName, binderOptions =>
            {
                binderOptions.ErrorOnUnknownConfiguration = true;
            })
            .ValidateOnStart();

        return services;
    }

    private static Func<IServiceProvider, IPlatformCredentialStore> CreatePlatformCredentialStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return _ => new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return _ => new MacOsKeychainCredentialStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return serviceProvider => new LinuxSecretToolCredentialStore(
                serviceProvider.GetRequiredService<IProcessRunner>());
        }

        return _ => new UnsupportedPlatformCredentialStore();
    }
}
