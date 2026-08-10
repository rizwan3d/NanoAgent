using StemCode.Application.Abstractions;
using StemCode.Application.Models;
using StemCode.Application.Tools;
using StemCode.Application.Tools.Models;
using StemCode.Infrastructure.Secrets;
using StemCode.Infrastructure.Tools;
using StemCode.Infrastructure.WindowsSandbox;

namespace StemCode.Infrastructure.CustomTools;

internal sealed class CustomToolProcessExecutor
{
    private readonly IProcessRunner _processRunner;
    private readonly PermissionSettings _permissionSettings;
    private readonly IWindowsSandboxProcessRunner? _windowsSandboxProcessRunner;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;

    public CustomToolProcessExecutor(
        IProcessRunner processRunner,
        IWorkspaceRootProvider workspaceRootProvider,
        PermissionSettings permissionSettings,
        IWindowsSandboxProcessRunner? windowsSandboxProcessRunner = null)
    {
        _processRunner = processRunner;
        _workspaceRootProvider = workspaceRootProvider;
        _permissionSettings = permissionSettings;
        _windowsSandboxProcessRunner = windowsSandboxProcessRunner;
    }

    public Task<ProcessExecutionResult> ExecuteAsync(
        CustomToolConfiguration configuration,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string workingDirectory = ResolveWorkingDirectory(configuration, context);
        string workspaceRoot = Path.GetFullPath(_workspaceRootProvider.GetWorkspaceRoot());
        ProcessExecutionRequest baseRequest = new(
            configuration.Command!,
            configuration.Args,
            StandardInput: CustomToolJson.CreateToolInput(context, configuration.Name),
            WorkingDirectory: workingDirectory,
            MaxOutputCharacters: configuration.MaxOutputChars);

        ShellCommandSandboxPlan sandboxPlan = ShellCommandSandboxPlanner.Create(
            baseRequest,
            _permissionSettings.SandboxMode,
            workspaceRoot,
            workingDirectory);
        ProcessExecutionRequest request = sandboxPlan.Request with
        {
            EnvironmentVariables = CreateEnvironment(
                configuration,
                context,
                workspaceRoot,
                workingDirectory,
                sandboxPlan.Enforcement)
        };

        if (string.Equals(
                sandboxPlan.Enforcement,
                ShellCommandSandboxPlanner.WindowsSandboxEnforcement,
                StringComparison.Ordinal))
        {
            if (_windowsSandboxProcessRunner is null)
            {
                throw new InvalidOperationException(
                    "Windows OS sandboxing is not configured for custom tool execution.");
            }

            return _windowsSandboxProcessRunner.RunAsync(
                request,
                CreateWindowsSandboxExecutionContext(workspaceRoot, workingDirectory),
                cancellationToken);
        }

        return _processRunner.RunAsync(request, cancellationToken);
    }

    private string ResolveWorkingDirectory(
        CustomToolConfiguration configuration,
        ToolExecutionContext context)
    {
        string requestedPath = string.IsNullOrWhiteSpace(configuration.Cwd)
            ? context.Session.WorkspacePath
            : configuration.Cwd!;
        return Path.GetFullPath(requestedPath);
    }

    private IReadOnlyDictionary<string, string> CreateEnvironment(
        CustomToolConfiguration configuration,
        ToolExecutionContext context,
        string workspaceRoot,
        string workingDirectory,
        string sandboxEnforcement)
    {
        Dictionary<string, string> environment = new(configuration.Env, StringComparer.Ordinal);

        environment["STEMCODE_TOOL_NAME"] = context.ToolName;
        environment["STEMCODE_CUSTOM_TOOL_NAME"] = configuration.Name;
        environment["STEMCODE_SESSION_ID"] = context.Session.SessionId;
        environment["STEMCODE_WORKSPACE_PATH"] = context.Session.WorkspacePath;
        environment["STEMCODE_WORKING_DIRECTORY"] = context.Session.WorkingDirectory;
        environment["STEMCODE_SANDBOX_MODE"] = ToWireValue(_permissionSettings.SandboxMode);
        environment["STEMCODE_SANDBOX_EFFECTIVE_MODE"] = ToWireValue(_permissionSettings.SandboxMode);
        environment["STEMCODE_SANDBOX_ENFORCEMENT"] = sandboxEnforcement;
        environment["STEMCODE_SANDBOX_PERMISSIONS"] = ShellCommandSandboxArguments.ToWireValue(
            ShellCommandSandboxPermissions.UseDefault);
        environment["STEMCODE_WORKSPACE_ROOT"] = workspaceRoot;
        environment["STEMCODE_CUSTOM_TOOL_SOURCE"] = configuration.SourcePath ?? string.Empty;
        environment["STEMCODE_CUSTOM_TOOL_TRUSTED"] = configuration.UntrustedWorkspaceDefinition ? "0" : "1";
        environment["STEMCODE_CUSTOM_TOOL_WORKING_DIRECTORY"] = workingDirectory;

        return environment;
    }

    private WindowsSandboxExecutionContext CreateWindowsSandboxExecutionContext(
        string workspaceRoot,
        string workingDirectory)
    {
        IReadOnlyList<string> writableRoots = _permissionSettings.SandboxMode == ToolSandboxMode.WorkspaceWrite
            ? [workspaceRoot]
            : [];

        return new WindowsSandboxExecutionContext(
            _permissionSettings.SandboxMode,
            WindowsSandboxPaths.ResolveAppHome(),
            workspaceRoot,
            workingDirectory,
            writableRoots,
            IncludeTempEnvironmentVariables: _permissionSettings.SandboxMode == ToolSandboxMode.WorkspaceWrite);
    }

    private static string ToWireValue(ToolSandboxMode sandboxMode)
    {
        return sandboxMode switch
        {
            ToolSandboxMode.ReadOnly => "read-only",
            ToolSandboxMode.DangerFullAccess => "danger-full-access",
            _ => "workspace-write"
        };
    }
}
