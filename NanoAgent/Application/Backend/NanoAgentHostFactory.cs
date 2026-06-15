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
        return Create(
            uiBridge,
            BackendRuntimeArguments.Parse(args),
            [],
            autoApproveAllTools: false);
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
        bool autoApproveAllTools,
        Action<IServiceCollection>? configureServices = null)
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
            runtimeArguments.EffectiveAppSurface(BackendRuntimeOptions.CliSurface),
            ResolveStartupPromptPreference(runtimeArguments.RawArgs)));
        builder.Services
            .AddApplication()
            .AddReplCommands()
            .AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton<ISelectionPrompt, UiSelectionPrompt>();
        builder.Services.AddSingleton<ITextPrompt, UiTextPrompt>();
        builder.Services.AddSingleton<ISecretPrompt, UiSecretPrompt>();
        builder.Services.AddSingleton<IConfirmationPrompt, UiConfirmationPrompt>();
        builder.Services.AddSingleton<IStatusMessageWriter, UiStatusMessageWriter>();

        // SDK/embedder extension point: runs after Application + Infrastructure + the
        // default UI prompt registrations so callers can override services (for example
        // an in-memory provider configuration to skip interactive onboarding, a fixed
        // workspace root, or custom ITool registrations) and add their own.
        configureServices?.Invoke(builder.Services);

        return builder.Build();
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
