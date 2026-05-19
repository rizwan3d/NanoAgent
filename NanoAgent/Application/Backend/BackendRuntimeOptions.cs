namespace NanoAgent.Application.Backend;

public sealed class BackendRuntimeOptions
{
    public BackendRuntimeOptions(
        IReadOnlyList<BackendMcpServerConfiguration>? sessionMcpServers = null,
        bool autoApproveAllTools = false,
        string? appSurface = null,
        bool enableStartupPrompts = false)
    {
        SessionMcpServers = sessionMcpServers ?? [];
        AutoApproveAllTools = autoApproveAllTools;
        AppSurface = NormalizeAppSurface(appSurface);
        EnableStartupPrompts = enableStartupPrompts;
    }

    public const string CliSurface = "cli";
    public const string DesktopSurface = "desktop";
    public const string JetBrainsSurface = "jetbrains";
    public const string VisualStudioSurface = "visual_studio";
    public const string VsCodeSurface = "vscode";

    public bool AutoApproveAllTools { get; }

    public string AppSurface { get; }

    public bool EnableStartupPrompts { get; }

    public IReadOnlyList<BackendMcpServerConfiguration> SessionMcpServers { get; }

    public static string NormalizeAppSurface(string? appSurface)
    {
        if (string.IsNullOrWhiteSpace(appSurface))
        {
            return CliSurface;
        }

        string normalized = appSurface.Trim().ToLowerInvariant();
        return normalized switch
        {
            CliSurface => CliSurface,
            DesktopSurface => DesktopSurface,
            JetBrainsSurface => JetBrainsSurface,
            VisualStudioSurface => VisualStudioSurface,
            VsCodeSurface => VsCodeSurface,
            _ => CliSurface
        };
    }
}
