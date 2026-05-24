using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.DependencyInjection;
using NanoAgent.Application.UI;
using NanoAgent.Infrastructure.DependencyInjection;

namespace NanoAgent.Application.Backend;

public static class NanoAgentHostFactory
{
    public static IHost Create(
        IUiBridge uiBridge,
        string[] args)
    {
        return Create(uiBridge, BackendRuntimeArguments.Parse(args), []);
    }

    public static IHost Create(
        IUiBridge uiBridge,
        string[] args,
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers)
    {
        return Create(
            uiBridge,
            BackendRuntimeArguments.Parse(args),
            sessionMcpServers,
            autoApproveAllTools: false);
    }

    public static IHost Create(
        IUiBridge uiBridge,
        string[] args,
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers,
        bool autoApproveAllTools)
    {
        return Create(
            uiBridge,
            BackendRuntimeArguments.Parse(args),
            sessionMcpServers,
            autoApproveAllTools);
    }

    internal static IHost Create(
        IUiBridge uiBridge,
        BackendRuntimeArguments runtimeArguments,
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers,
        bool autoApproveAllTools)
    {
        ArgumentNullException.ThrowIfNull(uiBridge);
        ArgumentNullException.ThrowIfNull(runtimeArguments);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(runtimeArguments.RawArgs);

        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: false);

        builder.Configuration.AddJsonFile(
            Path.Combine(Directory.GetCurrentDirectory(), ".nanoagent", "agent-profile.json"),
            optional: true,
            reloadOnChange: false);

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        builder.Services.AddSingleton(uiBridge);
        builder.Services.AddSingleton(sessionMcpServers ?? []);
        builder.Services.AddSingleton(new BackendRuntimeOptions(
            sessionMcpServers,
            autoApproveAllTools,
            runtimeArguments.EffectiveAppSurface(BackendRuntimeOptions.CliSurface)));
        builder.Services
            .AddApplication()
            .AddReplCommands()
            .AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton<ISelectionPrompt, UiSelectionPrompt>();
        builder.Services.AddSingleton<ITextPrompt, UiTextPrompt>();
        builder.Services.AddSingleton<ISecretPrompt, UiSecretPrompt>();
        builder.Services.AddSingleton<IConfirmationPrompt, UiConfirmationPrompt>();
        builder.Services.AddSingleton<IStatusMessageWriter, UiStatusMessageWriter>();

        return builder.Build();
    }
}
