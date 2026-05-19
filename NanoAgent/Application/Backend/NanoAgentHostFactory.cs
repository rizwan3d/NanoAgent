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
        return Create(uiBridge, args, []);
    }

    public static IHost Create(
        IUiBridge uiBridge,
        string[] args,
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers)
    {
        return Create(
            uiBridge,
            args,
            sessionMcpServers,
            autoApproveAllTools: false);
    }

    public static IHost Create(
        IUiBridge uiBridge,
        string[] args,
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers,
        bool autoApproveAllTools)
    {
        ArgumentNullException.ThrowIfNull(uiBridge);
        ArgumentNullException.ThrowIfNull(args);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

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
            ResolveAppSurface(args),
            ResolveStartupPromptPreference(args)));
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

    private static string ResolveAppSurface(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, "--surface", StringComparison.OrdinalIgnoreCase))
            {
                int valueIndex = index + 1;
                if (valueIndex < args.Count && !string.IsNullOrWhiteSpace(args[valueIndex]))
                {
                    return BackendRuntimeOptions.NormalizeAppSurface(args[valueIndex]);
                }

                break;
            }

            const string Prefix = "--surface=";
            if (arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return BackendRuntimeOptions.NormalizeAppSurface(arg[Prefix.Length..]);
            }
        }

        return BackendRuntimeOptions.CliSurface;
    }

    private static bool ResolveStartupPromptPreference(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            if (TryReadOptionValue(args, ref index, "--startup-prompts", out string? value))
            {
                return string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string? value)
    {
        string arg = args[index];
        value = null;

        if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
        {
            int valueIndex = index + 1;
            if (valueIndex >= args.Count || string.IsNullOrWhiteSpace(args[valueIndex]))
            {
                return false;
            }

            value = args[valueIndex].Trim();
            index = valueIndex;
            return true;
        }

        string prefix = optionName + "=";
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = arg[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(value);
    }
}
