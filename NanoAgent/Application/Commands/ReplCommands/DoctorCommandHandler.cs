using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Utilities;
using NanoAgent.Domain.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace NanoAgent.Application.Commands;

internal sealed class DoctorCommandHandler : IReplCommandHandler
{
    private readonly IBudgetControlsUsageService _budgetUsageService;
    private readonly ICodeIntelligenceService _codeIntelligenceService;
    private readonly IEnumerable<IDynamicToolProvider> _dynamicToolProviders;
    private readonly IToolRegistry _toolRegistry;
    private readonly IUserDataPathProvider _userDataPathProvider;
    private readonly ILogger<DoctorCommandHandler> _logger;
    private readonly PermissionSettings _permissionSettings;

    public DoctorCommandHandler(
        IBudgetControlsUsageService budgetUsageService,
        ICodeIntelligenceService codeIntelligenceService,
        IEnumerable<IDynamicToolProvider> dynamicToolProviders,
        IToolRegistry toolRegistry,
        IUserDataPathProvider userDataPathProvider,
        ILogger<DoctorCommandHandler> logger,
        PermissionSettings permissionSettings)
    {
        _budgetUsageService = budgetUsageService;
        _codeIntelligenceService = codeIntelligenceService;
        _dynamicToolProviders = dynamicToolProviders;
        _toolRegistry = toolRegistry;
        _userDataPathProvider = userDataPathProvider;
        _logger = logger;
        _permissionSettings = permissionSettings;
    }

    public string CommandName => "doctor";

    public string Description => "Show comprehensive system diagnostics for NanoAgent.";

    public string Usage => "/doctor";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        StringBuilder report = new();
        report.AppendLine("╔═══════════════════════════════════════════════");
        report.AppendLine("║  NanoAgent Doctor - System Diagnostics");
        report.AppendLine("╚═══════════════════════════════════════════════");
        report.AppendLine();

        AppendSystemInfo(report);
        AppendWorkspaceInfo(report, context);
        AppendConfigurationInfo(report);
        AppendSessionInfo(report, context);
        AppendProviderAndModelInfo(report, context);
        AppendProfileAndThinkingInfo(report, context);
        AppendPermissionInfo(report, context);
        AppendToolRegistryInfo(report);
        AppendDynamicToolProviders(report);
        await AppendLspStatusAsync(report, cancellationToken).ConfigureAwait(false);
        AppendSandboxInfo(report);
        await AppendBudgetStatusAsync(report, context, cancellationToken).ConfigureAwait(false);

        return ReplCommandResult.Continue(report.ToString().TrimEnd());
    }

    private static void AppendSystemInfo(StringBuilder report)
    {
        report.AppendLine("── System Information ──");
        report.AppendLine($"  OS:                 {RuntimeInformation.OSDescription}");
        report.AppendLine($"  OS Architecture:    {RuntimeInformation.OSArchitecture}");
        report.AppendLine($"  Process Architecture: {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($"  .NET Runtime:       {RuntimeInformation.FrameworkDescription}");
        report.AppendLine($"  Environment.Version: {Environment.Version}");
        report.AppendLine($"  Processors:         {Environment.ProcessorCount}");
        report.AppendLine($"  64-bit Process:     {Environment.Is64BitProcess}");
        report.AppendLine($"  User:               {Environment.UserName}");
        report.AppendLine($"  Machine:            {Environment.MachineName}");
        report.AppendLine($"  Command Line:       {Environment.CommandLine}");
        report.AppendLine();
    }

    private static void AppendWorkspaceInfo(StringBuilder report, ReplCommandContext context)
    {
        report.AppendLine("── Workspace ──");
        report.AppendLine($"  Current Directory:  {Directory.GetCurrentDirectory()}");
        report.AppendLine($"  Workspace Path:     {context.Session.WorkspacePath}");

        string nanoagentDir = Path.Combine(context.Session.WorkspacePath, ".nanoagent");
        report.AppendLine($"  .nanoagent:         {Directory.Exists(nanoagentDir)}");

        string profileFile = Path.Combine(nanoagentDir, "agent-profile.json");
        report.AppendLine($"  agent-profile.json: {File.Exists(profileFile)}");

        string memDir = Path.Combine(nanoagentDir, "memory");
        report.AppendLine($"  .nanoagent/memory:  {Directory.Exists(memDir)}");

        string skillsDir = Path.Combine(nanoagentDir, "skills");
        report.AppendLine($"  .nanoagent/skills:  {Directory.Exists(skillsDir)}");
        report.AppendLine();
    }

    private void AppendConfigurationInfo(StringBuilder report)
    {
        report.AppendLine("── Configuration Paths ──");
        report.AppendLine($"  Config File:        {_userDataPathProvider.GetConfigurationFilePath()}");
        report.AppendLine($"  MCP Config File:    {_userDataPathProvider.GetMcpConfigurationFilePath()}");

        string logsDir = _userDataPathProvider.GetLogsDirectoryPath();
        report.AppendLine($"  Logs Directory:     {logsDir} (exists: {Directory.Exists(logsDir)})");

        string sessionsDir = _userDataPathProvider.GetSessionsDirectoryPath();
        report.AppendLine($"  Sessions Directory: {sessionsDir} (exists: {Directory.Exists(sessionsDir)})");
        report.AppendLine();
    }

    private static void AppendSessionInfo(StringBuilder report, ReplCommandContext context)
    {
        report.AppendLine("── Session ──");
        report.AppendLine($"  Session ID:         {context.Session.SessionId}");
        report.AppendLine($"  Parent Session ID:  {context.Session.ParentSessionId ?? "(standalone)"}");
        report.AppendLine($"  Section Title:      {context.Session.SectionTitle}");
        report.AppendLine($"  Session Created:    {context.Session.SectionCreatedAtUtc:u}");
        report.AppendLine($"  Session Updated:    {context.Session.SectionUpdatedAtUtc:u}");
        report.AppendLine($"  Resume Command:     {context.Session.SessionResumeCommand}");
        report.AppendLine($"  Working Directory:  {context.Session.WorkingDirectory}");
        report.AppendLine($"  Conversation Turns: {context.Session.ConversationTurns.Count}");
        report.AppendLine($"  Estimated Tokens:   {context.Session.TotalEstimatedOutputTokens:N0}");
        report.AppendLine($"  Is Resumed Section: {context.Session.IsResumedSection}");
        report.AppendLine($"  Has Pending Plan:   {context.Session.HasPendingExecutionPlan}");
        report.AppendLine();
    }

    private void AppendProviderAndModelInfo(StringBuilder report, ReplCommandContext context)
    {
        report.AppendLine("── Provider & Model ──");
        report.AppendLine($"  Provider Name:      {context.Session.ProviderName}");

        string baseUrl = context.Session.ProviderProfile.ProviderKind.GetManagedBaseUrl()
            ?? context.Session.ProviderProfile.BaseUrl
            ?? "(not configured)";
        report.AppendLine($"  Provider Kind:      {context.Session.ProviderProfile.ProviderKind}");
        report.AppendLine($"  Base URL:           {baseUrl}");

        report.AppendLine($"  Active Provider:    {context.Session.ActiveProviderName ?? "(default)"}");
        report.AppendLine($"  Active Model:       {context.Session.ActiveModelId.ToDisplayNameWithProvider(context.Session.ProviderName)}");
        report.AppendLine($"  Available Models:   {context.Session.AvailableModelIds.Count} total");

        int? contextWindow = context.Session.ActiveModelContextWindowTokens;
        report.AppendLine($"  Context Window:     {(contextWindow.HasValue ? $"{contextWindow.Value:N0} tokens" : "unknown")}");

        if (context.Session.ModelContextWindowTokens.Count > 0)
        {
            report.AppendLine($"  Known Context Windows:");
            foreach ((string modelId, int window) in context.Session.ModelContextWindowTokens
                         .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal))
            {
                report.AppendLine($"    - {modelId.ToDisplayName()}: {window:N0} tokens");
            }
        }
        report.AppendLine();
    }

    private static void AppendProfileAndThinkingInfo(StringBuilder report, ReplCommandContext context)
    {
        report.AppendLine("── Profile & Thinking ──");
        report.AppendLine($"  Active Profile:     {context.Session.AgentProfile.Name}");
        report.AppendLine($"  Profile Mode:       {context.Session.AgentProfile.Mode}");
        report.AppendLine($"  Profile Description: {context.Session.AgentProfile.Description}");
        report.AppendLine($"  Thinking:           {ThinkingModeOptions.Format(context.Session.ThinkingMode)}");
        report.AppendLine($"  Reasoning effort:   {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}");
        report.AppendLine();
    }

    private void AppendPermissionInfo(StringBuilder report, ReplCommandContext context)
    {
        report.AppendLine("── Permission Settings ──");
        report.AppendLine($"  Default Mode:       {_permissionSettings.DefaultMode}");
        report.AppendLine($"  Sandbox Mode:       {_permissionSettings.SandboxMode}");
        report.AppendLine($"  Auto-Approve All:   {_permissionSettings.AutoApproveAllTools}");

        int configuredRuleCount = _permissionSettings.Rules?.Length ?? 0;
        report.AppendLine($"  Configured Rules:   {configuredRuleCount}");

        int overrideCount = context.Session.PermissionOverrides.Count;
        report.AppendLine($"  Session Overrides:  {overrideCount}");

        if (configuredRuleCount > 0 || overrideCount > 0)
        {
            int totalRules = configuredRuleCount + overrideCount;
            report.AppendLine($"  Total Effective:    {totalRules}");
        }

        report.AppendLine();
    }

    private void AppendToolRegistryInfo(StringBuilder report)
    {
        report.AppendLine("── Registered Tools ──");
        IReadOnlyList<ToolDefinition> definitions = _toolRegistry.GetToolDefinitions();
        IReadOnlyList<string> names = _toolRegistry.GetRegisteredToolNames();

        int builtInCount = definitions.Count(static d =>
            !d.Name.StartsWith(AgentToolNames.McpToolPrefix, StringComparison.Ordinal) &&
            !d.Name.StartsWith(AgentToolNames.CustomToolPrefix, StringComparison.Ordinal));

        int dynamicCount = definitions.Count - builtInCount;

        report.AppendLine($"  Total Definitions:  {definitions.Count}");
        report.AppendLine($"  Built-in Tools:     {builtInCount}");
        report.AppendLine($"  Dynamic Tools:      {dynamicCount}");
        report.AppendLine($"  Registered Names:   {names.Count}");

        // List built-in tool names
        string[] builtInNames = definitions
            .Where(static d =>
                !d.Name.StartsWith(AgentToolNames.McpToolPrefix, StringComparison.Ordinal) &&
                !d.Name.StartsWith(AgentToolNames.CustomToolPrefix, StringComparison.Ordinal))
            .Select(static d => d.Name)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();

        if (builtInNames.Length > 0)
        {
            report.AppendLine("  Built-in Tools:");
            foreach (string name in builtInNames)
            {
                report.AppendLine($"    - {name}");
            }
        }

        report.AppendLine();
    }

    private void AppendDynamicToolProviders(StringBuilder report)
    {
        report.AppendLine("── MCP & Dynamic Tool Providers ──");

        DynamicToolProviderStatus[] statuses = _dynamicToolProviders
            .SelectMany(static provider => provider.GetStatuses())
            .OrderBy(static status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (statuses.Length == 0)
        {
            report.AppendLine("  (none configured)");
        }
        else
        {
            foreach (DynamicToolProviderStatus status in statuses)
            {
                string state = status.Enabled
                    ? (status.IsAvailable ? "available" : "unavailable")
                    : "disabled";
                string details = string.IsNullOrWhiteSpace(status.Details)
                    ? string.Empty
                    : $", {status.Details}";

                report.AppendLine($"  - {status.Name} ({status.Kind}): {state}, {status.ToolCount} tool(s){details}");
            }
        }

        // Dynamic tools from registry
        string[] dynamicToolNames = _toolRegistry.GetToolDefinitions()
            .Select(static definition => definition.Name)
            .Where(static name =>
                name.StartsWith(AgentToolNames.McpToolPrefix, StringComparison.Ordinal) ||
                name.StartsWith(AgentToolNames.CustomToolPrefix, StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        report.AppendLine();
        if (dynamicToolNames.Length == 0)
        {
            report.AppendLine("  Dynamic Tools: (none)");
        }
        else
        {
            report.AppendLine($"  Dynamic Tools ({dynamicToolNames.Length}):");
            foreach (string toolName in dynamicToolNames)
            {
                report.AppendLine($"    - {toolName}");
            }
        }

        report.AppendLine();
    }

    private async Task AppendLspStatusAsync(StringBuilder report, CancellationToken cancellationToken)
    {
        report.AppendLine("── Language Server Protocol (LSP) ──");

        try
        {
            CodeIntelligenceResult result = await _codeIntelligenceService.QueryAsync(
                new CodeIntelligenceRequest(
                    "servers_status",
                    ".",
                    null,
                    null,
                    false,
                    10,
                    Refresh: false),
                cancellationToken);

            if (result.Servers is null || result.Servers.Count == 0)
            {
                report.AppendLine("  No language servers registered.");
            }
            else
            {
                report.AppendLine($"  Registered languages: {result.Servers.Count}");

                foreach (CodeIntelligenceServerStatus server in result.Servers
                             .OrderBy(static s => s.Language, StringComparer.OrdinalIgnoreCase))
                {
                    string extensions = string.Join(", ", server.FileExtensions);
                    report.AppendLine($"  - {server.Language} [{extensions}]");

                    if (!string.IsNullOrWhiteSpace(server.SelectedServerName))
                    {
                        report.AppendLine($"    Selected: {server.SelectedServerName}");
                    }

                    foreach (CodeIntelligenceServerCandidate candidate in server.Candidates)
                    {
                        string resolved = string.IsNullOrWhiteSpace(candidate.ResolvedCommand)
                            ? candidate.Command
                            : $"{candidate.Command} → {candidate.ResolvedCommand}";
                        string message = string.IsNullOrWhiteSpace(candidate.Message)
                            ? string.Empty
                            : $" ({candidate.Message})";
                        report.AppendLine($"    · [{candidate.DetectionStatus}] {candidate.Name} ({resolved}){message}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to query LSP status for doctor command.");
            report.AppendLine($"  (query failed: {exception.Message})");
        }

        report.AppendLine();
    }

    private static void AppendSandboxInfo(StringBuilder report)
    {
        report.AppendLine("── Windows Sandbox ──");

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        report.AppendLine($"  Platform:           {(isWindows ? "Windows" : RuntimeInformation.OSDescription)}");
        report.AppendLine($"  Sandbox Available:  {isWindows}");

        // Detect if we're running inside a sandbox
        bool isInSandbox = IsRunningInWindowsSandbox();
        if (isWindows)
        {
            report.AppendLine($"  Inside Sandbox:     {isInSandbox}");
        }

        report.AppendLine();
    }

    private static bool IsRunningInWindowsSandbox()
    {
        try
        {
            string? sandboxFlag = Environment.GetEnvironmentVariable("NANOAGENT_SANDBOX");
            if (!string.IsNullOrWhiteSpace(sandboxFlag) &&
                string.Equals(sandboxFlag, "1", StringComparison.Ordinal))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private async Task AppendBudgetStatusAsync(
        StringBuilder report,
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        report.AppendLine("── Budget Controls ──");

        try
        {
            BudgetControlsStatus status = await _budgetUsageService.GetStatusAsync(
                context.Session,
                cancellationToken);

            if (status is null)
            {
                report.AppendLine("  Budget status unavailable.");
            }
            else
            {
                report.AppendLine($"  Source:             {status.Source}");
                report.AppendLine($"  Spent:              ${status.SpentUsd:F4}");
                if (status.MonthlyBudgetUsd.HasValue)
                {
                    report.AppendLine($"  Monthly Budget:     ${status.MonthlyBudgetUsd.Value:F4}");
                }
                report.AppendLine($"  Alert Threshold:    {status.AlertThresholdPercent}%");
                report.AppendLine($"  Local Path:         {status.LocalPath ?? "(none)"}");
                report.AppendLine($"  Cloud API:          {(string.IsNullOrWhiteSpace(status.CloudApiUrl) ? "(none)" : status.CloudApiUrl)}");
                report.AppendLine($"  Has Cloud Auth:     {status.HasCloudAuthKey}");
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to query budget status for doctor command.");
            report.AppendLine($"  (query failed: {exception.Message})");
        }

        report.AppendLine();
    }
}
