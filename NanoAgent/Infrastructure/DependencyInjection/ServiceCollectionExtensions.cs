using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.BudgetControls;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.CodeIntelligence;
using NanoAgent.Infrastructure.Configuration;
using NanoAgent.Infrastructure.Conversation;
using NanoAgent.Infrastructure.CustomTools;
using NanoAgent.Infrastructure.GitHub;
using NanoAgent.Infrastructure.Hooks;
using NanoAgent.Infrastructure.Logging;
using NanoAgent.Infrastructure.Mcp;
using NanoAgent.Infrastructure.Models;
using NanoAgent.Infrastructure.OpenAi;
using NanoAgent.Infrastructure.Secrets;
using NanoAgent.Infrastructure.Storage;
using NanoAgent.Infrastructure.Tools;
using NanoAgent.Infrastructure.Updates;

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
        services.AddSingleton(static serviceProvider =>
            AgentProfileConfigurationReader.LoadMemorySettings(
                serviceProvider.GetRequiredService<IUserDataPathProvider>(),
                serviceProvider.GetRequiredService<IWorkspaceRootProvider>()));
        services.AddSingleton(static serviceProvider =>
            AgentProfileConfigurationReader.LoadToolAuditSettings(
                serviceProvider.GetRequiredService<IUserDataPathProvider>(),
                serviceProvider.GetRequiredService<IWorkspaceRootProvider>()));
        services.AddSingleton<IWorkspaceFileService, WorkspaceFileService>();
        services.AddSingleton<IWorkspaceSettingsWriter, WorkspaceSettingsWriter>();
        services.AddSingleton<ICodebaseIndexService, WorkspaceCodebaseIndexService>();
        services.AddSingleton<ICodeIntelligenceService, LspCodeIntelligenceService>();
        services.AddSingleton<IHeadlessBrowserService, HeadlessBrowserService>();
        services.AddSingleton<IWorkspaceSystemPromptProvider, WorkspaceSystemPromptProvider>();
        services.AddSingleton<IWorkspaceAgentProfilePromptProvider, WorkspaceAgentProfilePromptProvider>();
        services.AddSingleton<IWorkspaceInstructionsProvider, WorkspaceInstructionsProvider>();
        services.AddSingleton<ISkillService, WorkspaceSkillService>();
        services.AddSingleton<ILifecycleHookService, ShellLifecycleHookService>();
        services.AddSingleton<ILessonFailureClassifier, LessonFailureClassifier>();
        services.AddSingleton<ILessonMemoryService, WorkspaceLessonMemoryService>();
        services.AddSingleton<IToolAuditLogService, WorkspaceToolAuditLogService>();
        services.AddSingleton<IShellCommandService, ShellCommandService>();
        services.AddSingleton<NanoAgentMcpConfigLoader>();
        services.AddSingleton<IDynamicToolProvider, CustomToolDynamicProvider>();
        services.AddSingleton<IDynamicToolProvider, McpDynamicToolProvider>();
        services.AddSingleton(static serviceProvider =>
            ApplicationSettingsFactory.CreatePermissionSettings(
                serviceProvider.GetRequiredService<IOptions<ApplicationOptions>>().Value));
        services.AddHttpClient<IWebRunService, WebRunService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddHttpClient<IApplicationUpdateService, GitHubApplicationUpdateService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddHttpClient<IBudgetControlsUsageService, BudgetControlsUsageService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddHttpClient<IOpenAiCodexClientVersionProvider, GitHubOpenAiCodexClientVersionProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddHttpClient<OpenAiChatGptAccountCredentialService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddHttpClient<AnthropicClaudeAccountCredentialService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddHttpClient<GitHubCopilotCredentialService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddTransient<IOpenAiChatGptAccountCredentialService>(serviceProvider =>
            serviceProvider.GetRequiredService<OpenAiChatGptAccountCredentialService>());
        services.AddTransient<IOpenAiChatGptAccountAuthenticator>(serviceProvider =>
            serviceProvider.GetRequiredService<OpenAiChatGptAccountCredentialService>());
        services.AddTransient<IAnthropicClaudeAccountCredentialService>(serviceProvider =>
            serviceProvider.GetRequiredService<AnthropicClaudeAccountCredentialService>());
        services.AddTransient<IAnthropicClaudeAccountAuthenticator>(serviceProvider =>
            serviceProvider.GetRequiredService<AnthropicClaudeAccountCredentialService>());
        services.AddTransient<IGitHubCopilotCredentialService>(serviceProvider =>
            serviceProvider.GetRequiredService<GitHubCopilotCredentialService>());
        services.AddTransient<IGitHubCopilotAuthenticator>(serviceProvider =>
            serviceProvider.GetRequiredService<GitHubCopilotCredentialService>());
        services.AddHttpClient("NanoAgent.Mcp", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NanoAgent/1.0");
        });
        services.AddSingleton<IAgentConfigurationStore, JsonAgentConfigurationStore>();
        services.AddSingleton<IApiKeySecretStore, ApiKeySecretStore>();
        services.AddSingleton<IBudgetControlsConfigurationStore, JsonBudgetControlsConfigurationStore>();
        services.AddSingleton<IBudgetControlsSecretStore, BudgetControlsSecretStore>();
        services.AddSingleton<IModelCache, InMemoryModelCache>();
        services.AddSingleton<IConversationConfigurationAccessor, ConversationConfigurationAccessor>();
        services.AddSingleton<IConversationResponseMapper, OpenAiConversationResponseMapper>();
        services.AddSingleton(static serviceProvider =>
            ApplicationSettingsFactory.CreateModelSelectionSettings(
                serviceProvider.GetRequiredService<IOptions<ApplicationOptions>>().Value));
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
