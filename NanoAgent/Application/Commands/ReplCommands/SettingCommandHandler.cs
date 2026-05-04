using Microsoft.Extensions.DependencyInjection;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;

namespace NanoAgent.Application.Commands;

internal sealed class SettingCommandHandler : IReplCommandHandler
{
    private readonly IAgentConfigurationStore _configurationStore;
    private readonly IEnumerable<IDynamicToolProvider> _dynamicToolProviders;
    private readonly IAgentProfileResolver _profileResolver;
    private readonly IModelActivationService _modelActivationService;
    private readonly PermissionSettings _permissionSettings;
    private readonly ISelectionPrompt _selectionPrompt;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITextPrompt _textPrompt;
    private readonly IToolRegistry _toolRegistry;
    private readonly IWorkspaceSettingsWriter _workspaceSettingsWriter;

    public SettingCommandHandler(
        ISelectionPrompt selectionPrompt,
        IAgentProfileResolver profileResolver,
        IModelActivationService modelActivationService,
        IAgentConfigurationStore configurationStore,
        PermissionSettings permissionSettings,
        IServiceProvider serviceProvider,
        ITextPrompt textPrompt,
        IEnumerable<IDynamicToolProvider> dynamicToolProviders,
        IToolRegistry toolRegistry,
        IWorkspaceSettingsWriter workspaceSettingsWriter)
    {
        _selectionPrompt = selectionPrompt;
        _profileResolver = profileResolver;
        _modelActivationService = modelActivationService;
        _configurationStore = configurationStore;
        _permissionSettings = permissionSettings;
        _serviceProvider = serviceProvider;
        _textPrompt = textPrompt;
        _dynamicToolProviders = dynamicToolProviders;
        _toolRegistry = toolRegistry;
        _workspaceSettingsWriter = workspaceSettingsWriter;
    }

    public string CommandName => "setting";

    public string Description => "Open the NanoAgent settings picker for configurable session and workspace options.";

    public string Usage => "/setting [model|profile|thinking|provider|budget|workspace|permissions|tools|summary]";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Arguments.Count == 0)
        {
            return await RunSettingsMenuAsync(context, cancellationToken);
        }

        string action = context.Arguments[0].Trim();
        string remainingArgumentText = string.Join(' ', context.Arguments.Skip(1));
        return action.ToLowerInvariant() switch
        {
            "model" or "models" => remainingArgumentText.Length == 0
                ? await PromptForModelAsync(context, silent: false, cancellationToken) ?? ReplCommandResult.Continue()
                : await ExecuteCommandAsync("use", remainingArgumentText, context, cancellationToken),
            "profile" or "profiles" => remainingArgumentText.Length == 0
                ? await PromptForProfileAsync(context, silent: false, cancellationToken) ?? ReplCommandResult.Continue()
                : await ExecuteCommandAsync("profile", remainingArgumentText, context, cancellationToken),
            "thinking" => remainingArgumentText.Length == 0
                ? await PromptForThinkingAsync(context, silent: false, cancellationToken) ?? ReplCommandResult.Continue()
                : await ExecuteCommandAsync("thinking", remainingArgumentText, context, cancellationToken),
            "provider" or "providers" => remainingArgumentText.Length == 0
                ? await ExecuteCommandAsync("provider", string.Empty, context, cancellationToken)
                : await ExecuteCommandAsync("provider", remainingArgumentText, context, cancellationToken),
            "onboard" or "onboarding" => remainingArgumentText.Length == 0
                ? await ExecuteCommandAsync("onboard", string.Empty, context, cancellationToken)
                : ReplCommandResult.Continue("Usage: /setting onboarding", ReplFeedbackKind.Error),
            "budget" => await ExecuteCommandAsync("budget", remainingArgumentText, context, cancellationToken),
            "workspace" or "init" => await ExecuteCommandAsync("init", remainingArgumentText, context, cancellationToken),
            "permissions" or "permission" => remainingArgumentText.Length == 0
                ? await RunPermissionsMenuAsync(context, returnToSettingsMenu: false, cancellationToken) ?? ReplCommandResult.Continue()
                : ReplCommandResult.Continue("Usage: /setting permissions", ReplFeedbackKind.Error),
            "rules" => remainingArgumentText.Length == 0
                ? await PromptForRulesAsync(context, returnToSettingsMenu: false, cancellationToken) ?? ReplCommandResult.Continue()
                : ReplCommandResult.Continue("Usage: /setting rules", ReplFeedbackKind.Error),
            "tools" or "tool" or "mcp" or "custom-tools" => remainingArgumentText.Length == 0
                ? await PromptForToolsAsync(context, returnToSettingsMenu: false, cancellationToken) ?? ReplCommandResult.Continue()
                : ReplCommandResult.Continue("Usage: /setting tools", ReplFeedbackKind.Error),
            "summary" or "config" or "status" => remainingArgumentText.Length == 0
                ? await PromptForSummaryAsync(context, returnToSettingsMenu: false, cancellationToken) ?? ReplCommandResult.Continue()
                : ReplCommandResult.Continue("Usage: /setting summary", ReplFeedbackKind.Error),
            "help" or "-h" or "--help" => ReplCommandResult.Continue(FormatHelp()),
            _ => ReplCommandResult.Continue(
                $"Unknown settings area '{action}'.\n\n{FormatHelp()}",
                ReplFeedbackKind.Error)
        };
    }

    private async Task<ReplCommandResult> RunSettingsMenuAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            SettingCommandArea area;
            try
            {
                area = await PromptForAreaAsync(context, cancellationToken);
            }
            catch (PromptCancelledException)
            {
                return ReplCommandResult.Continue();
            }

            ReplCommandResult? result = await ExecuteAreaFromMenuAsync(
                area,
                context,
                cancellationToken);

            if (result is not null)
            {
                return result;
            }
        }
    }

    private async Task<SettingCommandArea> PromptForAreaAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        return await _selectionPrompt.PromptAsync(
            new SelectionPromptRequest<SettingCommandArea>(
                "NanoAgent settings",
                [
                    new SelectionPromptOption<SettingCommandArea>(
                        "Model",
                        SettingCommandArea.Model,
                        $"Pick the active model. Current: {context.Session.ActiveModelId}."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Profile",
                        SettingCommandArea.Profile,
                        $"Pick the active agent profile. Current: {context.Session.AgentProfile.Name}."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Thinking",
                        SettingCommandArea.Thinking,
                        $"Set thinking mode. Current: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Provider",
                        SettingCommandArea.Provider,
                        $"Switch saved providers or add one with /onboard. Current: {context.Session.ProviderName}."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Budget",
                        SettingCommandArea.Budget,
                        "Configure local or cloud budget controls."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Workspace",
                        SettingCommandArea.Workspace,
                        "Create or review workspace-local NanoAgent files."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Permissions",
                        SettingCommandArea.Permissions,
                        "Edit permission modes, sandbox behavior, and session overrides."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Tools",
                        SettingCommandArea.Tools,
                        "Inspect MCP servers, custom tools, and dynamic tool status."),
                    new SelectionPromptOption<SettingCommandArea>(
                        "Summary",
                        SettingCommandArea.Summary,
                        "Review provider, session, profile, thinking, and model details.")
                ],
                CreateCurrentSettingsSummary(context),
                DefaultIndex: 0,
                AllowCancellation: true),
            cancellationToken);
    }

    private async Task<ReplCommandResult?> ExecuteAreaFromMenuAsync(
        SettingCommandArea area,
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        return area switch
        {
            SettingCommandArea.Model => await PromptForModelAsync(context, silent: true, cancellationToken),
            SettingCommandArea.Profile => await PromptForProfileAsync(context, silent: true, cancellationToken),
            SettingCommandArea.Thinking => await PromptForThinkingAsync(context, silent: true, cancellationToken),
            SettingCommandArea.Provider => await ExecuteCommandFromMenuAsync("provider", string.Empty, context, cancellationToken),
            SettingCommandArea.Budget => await ExecuteCommandFromMenuAsync("budget", string.Empty, context, cancellationToken),
            SettingCommandArea.Workspace => await ExecuteCommandFromMenuAsync("init", string.Empty, context, cancellationToken),
            SettingCommandArea.Permissions => await RunPermissionsMenuAsync(context, returnToSettingsMenu: true, cancellationToken),
            SettingCommandArea.Tools => await PromptForToolsAsync(context, returnToSettingsMenu: true, cancellationToken),
            _ => await PromptForSummaryAsync(context, returnToSettingsMenu: true, cancellationToken)
        };
    }

    private async Task<ReplCommandResult?> PromptForModelAsync(
        ReplCommandContext context,
        bool silent,
        CancellationToken cancellationToken)
    {
        string selectedModel;
        try
        {
            selectedModel = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<string>(
                    "Choose active model",
                    context.Session.AvailableModelIds
                        .Select(modelId =>
                        {
                            bool active = string.Equals(
                                modelId,
                                context.Session.ActiveModelId,
                                StringComparison.Ordinal);

                            return new SelectionPromptOption<string>(
                                modelId,
                                modelId,
                                active ? "Currently active." : "Use this model for subsequent prompts.");
                        })
                        .ToArray(),
                    $"Provider: {context.Session.ProviderName}. Esc returns to settings.",
                    DefaultIndex: GetModelDefaultIndex(context.Session),
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return silent
                ? null
                : ReplCommandResult.Continue("Model selection cancelled.", ReplFeedbackKind.Warning);
        }

        ModelActivationResult result = _modelActivationService.Resolve(
            context.Session,
            selectedModel);

        if (result.Status == ModelActivationStatus.Switched &&
            !string.IsNullOrWhiteSpace(result.ResolvedModelId))
        {
            await _configurationStore.SaveAsync(
                new AgentConfiguration(
                    context.Session.ProviderProfile,
                    result.ResolvedModelId,
                    context.Session.ReasoningEffort,
                    context.Session.ActiveProviderName),
                cancellationToken);
        }

        if (silent)
        {
            return null;
        }

        return result.Status switch
        {
            ModelActivationStatus.Switched =>
                ReplCommandResult.Continue($"Active model switched to '{result.ResolvedModelId}'."),
            ModelActivationStatus.AlreadyActive =>
                ReplCommandResult.Continue($"Already using '{result.ResolvedModelId}'."),
            ModelActivationStatus.Ambiguous =>
                ReplCommandResult.Continue(
                    "Model name is ambiguous. Matches: " + string.Join(", ", result.CandidateModelIds),
                    ReplFeedbackKind.Error),
            _ =>
                ReplCommandResult.Continue(
                    $"Model '{selectedModel}' is not available. Use /models to choose from valid models.",
                    ReplFeedbackKind.Error)
        };
    }

    private async Task<ReplCommandResult?> PromptForProfileAsync(
        ReplCommandContext context,
        bool silent,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IAgentProfile> profiles = _profileResolver.List();
        if (profiles.Count == 0)
        {
            return ReplCommandResult.Continue(
                "No agent profiles are available.",
                ReplFeedbackKind.Error);
        }

        IAgentProfile selectedProfile;
        try
        {
            selectedProfile = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<IAgentProfile>(
                    "Choose active profile",
                    profiles
                        .Select(profile =>
                        {
                            bool isActive = string.Equals(
                                profile.Name,
                                context.Session.AgentProfile.Name,
                                StringComparison.OrdinalIgnoreCase);

                            return new SelectionPromptOption<IAgentProfile>(
                                profile.Name,
                                profile,
                                isActive
                                    ? "Currently active."
                                    : $"{profile.Mode.ToString().ToLowerInvariant()} - {profile.Description}");
                        })
                        .ToArray(),
                    "Profiles tune the agent's behavior for subsequent prompts in this session. Esc returns to settings.",
                    DefaultIndex: GetProfileDefaultIndex(profiles, context.Session.AgentProfile.Name),
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            if (silent)
            {
                return null;
            }

            return ReplCommandResult.Continue(
                "Profile selection cancelled.",
                ReplFeedbackKind.Warning);
        }

        if (string.Equals(
                context.Session.AgentProfile.Name,
                selectedProfile.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            if (silent)
            {
                return null;
            }

            return ReplCommandResult.Continue(
                $"Already using '{selectedProfile.Name}'.");
        }

        context.Session.SetAgentProfile(selectedProfile);
        if (silent)
        {
            return null;
        }

        return ReplCommandResult.Continue(
            $"Active agent profile switched to '{selectedProfile.Name}'. Subsequent prompts in this session will use the '{selectedProfile.Name}' profile.");
    }

    private async Task<ReplCommandResult?> PromptForThinkingAsync(
        ReplCommandContext context,
        bool silent,
        CancellationToken cancellationToken)
    {
        string selectedMode;
        try
        {
            selectedMode = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<string>(
                    "Choose thinking mode",
                    [
                        new SelectionPromptOption<string>(
                            "On",
                            ReasoningEffortOptions.On,
                            string.Equals(context.Session.ReasoningEffort, ReasoningEffortOptions.On, StringComparison.Ordinal)
                                ? "Currently active."
                                : "Use the model's default reasoning effort."),
                        new SelectionPromptOption<string>(
                            "Off",
                            ReasoningEffortOptions.Off,
                            string.Equals(context.Session.ReasoningEffort, ReasoningEffortOptions.Off, StringComparison.Ordinal)
                                ? "Currently active."
                                : "Use lighter responses without extra thinking.")
                    ],
                    "Thinking mode applies to subsequent prompts. Esc returns to settings.",
                    DefaultIndex: string.Equals(context.Session.ReasoningEffort, ReasoningEffortOptions.On, StringComparison.Ordinal)
                        ? 0
                        : 1,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            if (silent)
            {
                return null;
            }

            return ReplCommandResult.Continue(
                "Thinking selection cancelled.",
                ReplFeedbackKind.Warning);
        }

        bool modeChanged = context.Session.SetReasoningEffort(selectedMode);
        await SaveConfigurationAsync(context.Session, cancellationToken);

        if (silent)
        {
            return null;
        }

        return ReplCommandResult.Continue(
            modeChanged
                ? $"Thinking turned {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}."
                : $"Thinking is already {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}.");
    }

    private async Task<ReplCommandResult?> RunPermissionsMenuAsync(
        ReplCommandContext context,
        bool returnToSettingsMenu,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            SettingPermissionAction action;
            try
            {
                action = await _selectionPrompt.PromptAsync(
                    new SelectionPromptRequest<SettingPermissionAction>(
                        "Permission settings",
                        [
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Default fallback",
                                SettingPermissionAction.DefaultMode,
                                $"Current: {FormatPermissionMode(_permissionSettings.DefaultMode)}."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Sandbox mode",
                                SettingPermissionAction.SandboxMode,
                                $"Current: {FormatSandboxMode(_permissionSettings.SandboxMode)}."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Edits",
                                SettingPermissionAction.Edits,
                                "Set a session rule for file_write and apply_patch."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "File reads",
                                SettingPermissionAction.FileReads,
                                "Set a session rule for file_read."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Shell commands",
                                SettingPermissionAction.Shell,
                                "Set a session rule for bash/shell_command."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Network",
                                SettingPermissionAction.Network,
                                "Set a session rule for webfetch/web_run."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Memory writes",
                                SettingPermissionAction.MemoryWrite,
                                "Set a session rule for memory writes."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "MCP tools",
                                SettingPermissionAction.McpTools,
                                "Set a session rule for MCP tools."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Add allow rule",
                                SettingPermissionAction.AddAllow,
                                "Create a custom session allow override."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Add deny rule",
                                SettingPermissionAction.AddDeny,
                                "Create a custom session deny override."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Session overrides",
                                SettingPermissionAction.SessionOverrides,
                                $"View or clear current overrides. Count: {context.Session.PermissionOverrides.Count}."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Rules",
                                SettingPermissionAction.Rules,
                                $"Inspect configured and session rules without printing them. Count: {(_permissionSettings.Rules?.Length ?? 0) + context.Session.PermissionOverrides.Count}."),
                            new SelectionPromptOption<SettingPermissionAction>(
                                "Back",
                                SettingPermissionAction.Back,
                                "Return to NanoAgent settings.")
                        ],
                        "Default and sandbox changes are saved to .nanoagent/agent-profile.json. Rule changes here are session-scoped. Esc returns to NanoAgent settings.",
                        DefaultIndex: 0,
                        AllowCancellation: true),
                    cancellationToken);
            }
            catch (PromptCancelledException)
            {
                return returnToSettingsMenu
                    ? null
                    : ReplCommandResult.Continue();
            }

            if (action == SettingPermissionAction.Back)
            {
                return returnToSettingsMenu
                    ? null
                    : ReplCommandResult.Continue();
            }

            await ExecutePermissionActionAsync(context, action, cancellationToken);
        }
    }

    private async Task ExecutePermissionActionAsync(
        ReplCommandContext context,
        SettingPermissionAction action,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case SettingPermissionAction.DefaultMode:
                PermissionMode? defaultMode = await PromptForPermissionModeAsync(
                    "Default fallback mode",
                    _permissionSettings.DefaultMode,
                    "Used when no configured or session rule matches.",
                    cancellationToken);
                if (defaultMode is not null)
                {
                    _permissionSettings.DefaultMode = defaultMode.Value;
                    _permissionSettings.AutoApproveAllTools = defaultMode.Value == PermissionMode.Allow &&
                        _permissionSettings.AutoApproveAllTools;
                    await SaveWorkspacePermissionSettingsAsync(context, cancellationToken);
                }

                return;

            case SettingPermissionAction.SandboxMode:
                ToolSandboxMode? sandboxMode = await PromptForSandboxModeAsync(cancellationToken);
                if (sandboxMode is not null)
                {
                    _permissionSettings.SandboxMode = sandboxMode.Value;
                    await SaveWorkspacePermissionSettingsAsync(context, cancellationToken);
                }

                return;

            case SettingPermissionAction.Edits:
                await PromptAndAddToolOverrideAsync(
                    context,
                    "Edit permission",
                    ["file_write", AgentToolNames.ApplyPatch],
                    cancellationToken);
                return;

            case SettingPermissionAction.FileReads:
                await PromptAndAddToolOverrideAsync(
                    context,
                    "File read permission",
                    [AgentToolNames.FileRead],
                    cancellationToken);
                return;

            case SettingPermissionAction.Shell:
                await PromptAndAddToolOverrideAsync(
                    context,
                    "Shell permission",
                    ["bash", AgentToolNames.ShellCommand],
                    cancellationToken);
                return;

            case SettingPermissionAction.Network:
                await PromptAndAddToolOverrideAsync(
                    context,
                    "Network permission",
                    ["webfetch", AgentToolNames.WebRun],
                    cancellationToken);
                return;

            case SettingPermissionAction.MemoryWrite:
                await PromptAndAddToolOverrideAsync(
                    context,
                    "Memory write permission",
                    ["memory_write"],
                    cancellationToken);
                return;

            case SettingPermissionAction.McpTools:
                await PromptAndAddToolOverrideAsync(
                    context,
                    "MCP tool permission",
                    ["mcp"],
                    cancellationToken);
                return;

            case SettingPermissionAction.AddAllow:
                await PromptAndAddCustomOverrideAsync(
                    context,
                    PermissionMode.Allow,
                    cancellationToken);
                return;

            case SettingPermissionAction.AddDeny:
                await PromptAndAddCustomOverrideAsync(
                    context,
                    PermissionMode.Deny,
                    cancellationToken);
                return;

            case SettingPermissionAction.SessionOverrides:
                await PromptForSessionOverridesAsync(context, cancellationToken);
                return;

            case SettingPermissionAction.Rules:
                await PromptForRulesAsync(context, returnToSettingsMenu: true, cancellationToken);
                return;
        }
    }

    private async Task<PermissionMode?> PromptForPermissionModeAsync(
        string title,
        PermissionMode currentMode,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            SettingPermissionModeAction action = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SettingPermissionModeAction>(
                    title,
                    [
                        new SelectionPromptOption<SettingPermissionModeAction>(
                            "Allow",
                            SettingPermissionModeAction.Allow,
                            currentMode == PermissionMode.Allow ? "Currently active." : "Approve matching tool calls."),
                        new SelectionPromptOption<SettingPermissionModeAction>(
                            "Ask",
                            SettingPermissionModeAction.Ask,
                            currentMode == PermissionMode.Ask ? "Currently active." : "Prompt before matching tool calls."),
                        new SelectionPromptOption<SettingPermissionModeAction>(
                            "Deny",
                            SettingPermissionModeAction.Deny,
                            currentMode == PermissionMode.Deny ? "Currently active." : "Block matching tool calls."),
                        new SelectionPromptOption<SettingPermissionModeAction>(
                            "Back",
                            SettingPermissionModeAction.Back,
                            "Return to permission settings.")
                    ],
                    description + " Esc returns to permission settings.",
                    DefaultIndex: currentMode switch
                    {
                        PermissionMode.Allow => 0,
                        PermissionMode.Ask => 1,
                        PermissionMode.Deny => 2,
                        _ => 1
                    },
                    AllowCancellation: true),
                cancellationToken);

            return action switch
            {
                SettingPermissionModeAction.Allow => PermissionMode.Allow,
                SettingPermissionModeAction.Ask => PermissionMode.Ask,
                SettingPermissionModeAction.Deny => PermissionMode.Deny,
                _ => null
            };
        }
        catch (PromptCancelledException)
        {
            return null;
        }
    }

    private async Task<ToolSandboxMode?> PromptForSandboxModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            SettingSandboxModeAction action = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SettingSandboxModeAction>(
                    "Sandbox mode",
                    [
                        new SelectionPromptOption<SettingSandboxModeAction>(
                            "Read only",
                            SettingSandboxModeAction.ReadOnly,
                            _permissionSettings.SandboxMode == ToolSandboxMode.ReadOnly ? "Currently active." : "Block write-like tools and unsafe shell commands."),
                        new SelectionPromptOption<SettingSandboxModeAction>(
                            "Workspace write",
                            SettingSandboxModeAction.WorkspaceWrite,
                            _permissionSettings.SandboxMode == ToolSandboxMode.WorkspaceWrite ? "Currently active." : "Allow writes inside the active workspace with permission checks."),
                        new SelectionPromptOption<SettingSandboxModeAction>(
                            "Danger full access",
                            SettingSandboxModeAction.DangerFullAccess,
                            _permissionSettings.SandboxMode == ToolSandboxMode.DangerFullAccess ? "Currently active." : "Do not apply sandbox escalation limits."),
                        new SelectionPromptOption<SettingSandboxModeAction>(
                            "Back",
                            SettingSandboxModeAction.Back,
                            "Return to permission settings.")
                    ],
                    "Esc returns to permission settings.",
                    DefaultIndex: _permissionSettings.SandboxMode switch
                    {
                        ToolSandboxMode.ReadOnly => 0,
                        ToolSandboxMode.WorkspaceWrite => 1,
                        ToolSandboxMode.DangerFullAccess => 2,
                        _ => 1
                    },
                    AllowCancellation: true),
                cancellationToken);

            return action switch
            {
                SettingSandboxModeAction.ReadOnly => ToolSandboxMode.ReadOnly,
                SettingSandboxModeAction.WorkspaceWrite => ToolSandboxMode.WorkspaceWrite,
                SettingSandboxModeAction.DangerFullAccess => ToolSandboxMode.DangerFullAccess,
                _ => null
            };
        }
        catch (PromptCancelledException)
        {
            return null;
        }
    }

    private async Task PromptAndAddToolOverrideAsync(
        ReplCommandContext context,
        string title,
        string[] tools,
        CancellationToken cancellationToken)
    {
        PermissionMode? mode = await PromptForPermissionModeAsync(
            title,
            PermissionMode.Ask,
            "Adds a session override. Later session rules win when more than one rule matches.",
            cancellationToken);

        if (mode is null)
        {
            return;
        }

        context.Session.AddPermissionOverride(new PermissionRule
        {
            Mode = mode.Value,
            Tools = tools
        });
    }

    private async Task PromptAndAddCustomOverrideAsync(
        ReplCommandContext context,
        PermissionMode mode,
        CancellationToken cancellationToken)
    {
        string toolPattern;
        string? subjectPattern;
        try
        {
            toolPattern = await _textPrompt.PromptAsync(
                new TextPromptRequest(
                    "Tool or tag",
                    "Examples: file_write, apply_patch, bash, webfetch, mcp, memory_write.",
                    AllowCancellation: true),
                cancellationToken);

            subjectPattern = await _textPrompt.PromptAsync(
                new TextPromptRequest(
                    "Target pattern",
                    "Optional. Examples: src/**, docs/**, dotnet test*. Leave empty for all targets.",
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(toolPattern))
        {
            return;
        }

        PermissionCommandSupport.AddSessionOverride(
            context.Session,
            mode,
            toolPattern.Trim(),
            string.IsNullOrWhiteSpace(subjectPattern) ? null : subjectPattern.Trim());
    }

    private async Task PromptForSessionOverridesAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionRule> overrides = context.Session.PermissionOverrides;
        List<SelectionPromptOption<SettingSessionOverrideAction>> options =
        [
            new SelectionPromptOption<SettingSessionOverrideAction>(
                "Back",
                SettingSessionOverrideAction.Back,
                "Return to permission settings.")
        ];

        if (overrides.Count > 0)
        {
            options.Add(new SelectionPromptOption<SettingSessionOverrideAction>(
                "Clear session overrides",
                SettingSessionOverrideAction.Clear,
                $"Remove all {overrides.Count} session override rule(s)."));

            for (int index = 0; index < overrides.Count; index++)
            {
                options.Add(new SelectionPromptOption<SettingSessionOverrideAction>(
                    $"{index + 1}. {PermissionCommandSupport.FormatRule(overrides[index])}",
                    SettingSessionOverrideAction.Back,
                    "Session override. Add a later rule to supersede it."));
            }
        }

        try
        {
            SettingSessionOverrideAction action = await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SettingSessionOverrideAction>(
                    "Session overrides",
                    options,
                    overrides.Count == 0
                        ? "No session overrides are active. Esc returns to permission settings."
                        : "Session overrides only affect the current REPL session. Esc returns to permission settings.",
                    DefaultIndex: 0,
                    AllowCancellation: true),
                cancellationToken);

            if (action == SettingSessionOverrideAction.Clear)
            {
                context.Session.ClearPermissionOverrides();
            }
        }
        catch (PromptCancelledException)
        {
        }
    }

    private async Task<ReplCommandResult?> PromptForRulesAsync(
        ReplCommandContext context,
        bool returnToSettingsMenu,
        CancellationToken cancellationToken)
    {
        List<SelectionPromptOption<SettingRulesAction>> options =
        [
            new SelectionPromptOption<SettingRulesAction>(
                "Back",
                SettingRulesAction.Back,
                "Return to settings.")
        ];

        PermissionRule[] configuredRules = _permissionSettings.Rules ?? [];
        for (int index = 0; index < configuredRules.Length; index++)
        {
            options.Add(new SelectionPromptOption<SettingRulesAction>(
                $"Configured {index + 1}. {PermissionCommandSupport.FormatRule(configuredRules[index])}",
                SettingRulesAction.Back,
                "Configured or built-in rule."));
        }

        IReadOnlyList<PermissionRule> sessionRules = context.Session.PermissionOverrides;
        for (int index = 0; index < sessionRules.Count; index++)
        {
            options.Add(new SelectionPromptOption<SettingRulesAction>(
                $"Session {index + 1}. {PermissionCommandSupport.FormatRule(sessionRules[index])}",
                SettingRulesAction.Back,
                "Session override. Later rules win."));
        }

        try
        {
            await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SettingRulesAction>(
                    "Permission rules",
                    options,
                    "Later rules win when multiple rules match the same tool and target. Esc returns to settings.",
                    DefaultIndex: 0,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
        }

        return returnToSettingsMenu
            ? null
            : ReplCommandResult.Continue();
    }

    private async Task<ReplCommandResult?> PromptForSummaryAsync(
        ReplCommandContext context,
        bool returnToSettingsMenu,
        CancellationToken cancellationToken)
    {
        try
        {
            await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SettingSummaryAction>(
                    "Settings summary",
                    [
                        new SelectionPromptOption<SettingSummaryAction>(
                            $"Provider: {context.Session.ProviderName}",
                            SettingSummaryAction.Back,
                            "Choose Provider in settings to switch saved providers."),
                        new SelectionPromptOption<SettingSummaryAction>(
                            $"Model: {context.Session.ActiveModelId}",
                            SettingSummaryAction.Back,
                            "Choose Model in settings to change it."),
                        new SelectionPromptOption<SettingSummaryAction>(
                            $"Profile: {context.Session.AgentProfile.Name}",
                            SettingSummaryAction.Back,
                            "Choose Profile in settings to change it."),
                        new SelectionPromptOption<SettingSummaryAction>(
                            $"Thinking: {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}",
                            SettingSummaryAction.Back,
                            "Choose Thinking in settings to change it."),
                        new SelectionPromptOption<SettingSummaryAction>(
                            $"Permissions: {FormatPermissionMode(_permissionSettings.DefaultMode)}, sandbox {FormatSandboxMode(_permissionSettings.SandboxMode)}",
                            SettingSummaryAction.Back,
                            "Choose Permissions in settings to edit them."),
                        new SelectionPromptOption<SettingSummaryAction>(
                            "Back",
                            SettingSummaryAction.Back,
                            "Return to settings.")
                    ],
                    $"Session: {context.Session.SessionId}. Esc returns to settings.",
                    DefaultIndex: 0,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
        }

        return returnToSettingsMenu
            ? null
            : ReplCommandResult.Continue();
    }

    private async Task<ReplCommandResult?> PromptForToolsAsync(
        ReplCommandContext context,
        bool returnToSettingsMenu,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            SettingToolsAction action;
            try
            {
                action = await _selectionPrompt.PromptAsync(
                    new SelectionPromptRequest<SettingToolsAction>(
                        "Tool settings",
                        [
                            new SelectionPromptOption<SettingToolsAction>(
                                "Dynamic tools",
                                SettingToolsAction.DynamicTools,
                                "Inspect MCP servers, custom providers, and dynamic tools."),
                            new SelectionPromptOption<SettingToolsAction>(
                                "MCP permission",
                                SettingToolsAction.McpPermission,
                                "Edit the session permission mode for MCP tools."),
                            new SelectionPromptOption<SettingToolsAction>(
                                "Back",
                                SettingToolsAction.Back,
                                "Return to settings.")
                        ],
                        "Esc returns to settings.",
                        DefaultIndex: 0,
                        AllowCancellation: true),
                    cancellationToken);
            }
            catch (PromptCancelledException)
            {
                return returnToSettingsMenu
                    ? null
                    : ReplCommandResult.Continue();
            }

            switch (action)
            {
                case SettingToolsAction.DynamicTools:
                    await PromptForDynamicToolsAsync(cancellationToken);
                    break;

                case SettingToolsAction.McpPermission:
                    await PromptAndAddToolOverrideAsync(
                        context,
                        "MCP tool permission",
                        ["mcp"],
                        cancellationToken);
                    break;

                default:
                    return returnToSettingsMenu
                        ? null
                        : ReplCommandResult.Continue();
            }
        }
    }

    private async Task PromptForDynamicToolsAsync(CancellationToken cancellationToken)
    {
        DynamicToolProviderStatus[] statuses = _dynamicToolProviders
            .SelectMany(static provider => provider.GetStatuses())
            .OrderBy(static status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] toolNames = _toolRegistry.GetToolDefinitions()
            .Select(static definition => definition.Name)
            .Where(static name =>
                name.StartsWith(AgentToolNames.McpToolPrefix, StringComparison.Ordinal) ||
                name.StartsWith(AgentToolNames.CustomToolPrefix, StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        List<SelectionPromptOption<SettingDynamicToolsAction>> options =
        [
            new SelectionPromptOption<SettingDynamicToolsAction>(
                "Back",
                SettingDynamicToolsAction.Back,
                "Return to tool settings.")
        ];

        if (statuses.Length == 0)
        {
            options.Add(new SelectionPromptOption<SettingDynamicToolsAction>(
                "No dynamic providers",
                SettingDynamicToolsAction.Back,
                "No MCP or custom tool providers are configured."));
        }
        else
        {
            foreach (DynamicToolProviderStatus status in statuses)
            {
                string state = status.Enabled
                    ? status.IsAvailable ? "available" : "unavailable"
                    : "disabled";
                string details = string.IsNullOrWhiteSpace(status.Details)
                    ? $"{status.ToolCount} tool(s)."
                    : $"{status.ToolCount} tool(s). {status.Details}";

                options.Add(new SelectionPromptOption<SettingDynamicToolsAction>(
                    $"{status.Name} ({status.Kind}): {state}",
                    SettingDynamicToolsAction.Back,
                    details));
            }
        }

        if (toolNames.Length == 0)
        {
            options.Add(new SelectionPromptOption<SettingDynamicToolsAction>(
                "No dynamic tools",
                SettingDynamicToolsAction.Back,
                "No dynamic tools are currently available."));
        }
        else
        {
            foreach (string toolName in toolNames)
            {
                options.Add(new SelectionPromptOption<SettingDynamicToolsAction>(
                    toolName,
                    SettingDynamicToolsAction.Back,
                    "Dynamic tool."));
            }
        }

        try
        {
            await _selectionPrompt.PromptAsync(
                new SelectionPromptRequest<SettingDynamicToolsAction>(
                    "Dynamic tools",
                    options,
                    "Esc returns to tool settings.",
                    DefaultIndex: 0,
                    AllowCancellation: true),
                cancellationToken);
        }
        catch (PromptCancelledException)
        {
        }
    }

    private async Task<ReplCommandResult> ExecuteCommandAsync(
        string commandName,
        string argumentText,
        ReplCommandContext sourceContext,
        CancellationToken cancellationToken)
    {
        IReplCommandHandler handler = _serviceProvider
            .GetServices<IReplCommandHandler>()
            .FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, this) &&
                string.Equals(candidate.CommandName, commandName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"The '/{commandName}' command is not registered.");

        string normalizedArgumentText = argumentText.Trim();
        string[] arguments = normalizedArgumentText.Length == 0
            ? []
            : normalizedArgumentText.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string rawText = normalizedArgumentText.Length == 0
            ? $"/{commandName}"
            : $"/{commandName} {normalizedArgumentText}";

        return await handler.ExecuteAsync(
            new ReplCommandContext(
                commandName,
                normalizedArgumentText,
                arguments,
                rawText,
                sourceContext.Session),
            cancellationToken);
    }

    private async Task<ReplCommandResult?> ExecuteCommandFromMenuAsync(
        string commandName,
        string argumentText,
        ReplCommandContext sourceContext,
        CancellationToken cancellationToken)
    {
        try
        {
            ReplCommandResult result = await ExecuteCommandAsync(
                commandName,
                argumentText,
                sourceContext,
                cancellationToken);

            return result.FeedbackKind == ReplFeedbackKind.Warning &&
                result.Message?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true
                    ? null
                    : result;
        }
        catch (PromptCancelledException)
        {
            return null;
        }
    }

    private Task SaveConfigurationAsync(
        ReplSessionContext session,
        CancellationToken cancellationToken)
    {
        return _configurationStore.SaveAsync(
            new AgentConfiguration(
                session.ProviderProfile,
                session.ActiveModelId,
                session.ReasoningEffort,
                session.ActiveProviderName),
            cancellationToken);
    }

    private Task SaveWorkspacePermissionSettingsAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        return _workspaceSettingsWriter.SavePermissionSettingsAsync(
            context.Session.WorkspacePath,
            _permissionSettings,
            cancellationToken);
    }

    private static int GetProfileDefaultIndex(
        IReadOnlyList<IAgentProfile> profiles,
        string activeProfileName)
    {
        for (int index = 0; index < profiles.Count; index++)
        {
            if (string.Equals(profiles[index].Name, activeProfileName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static int GetModelDefaultIndex(ReplSessionContext session)
    {
        for (int index = 0; index < session.AvailableModelIds.Count; index++)
        {
            if (string.Equals(session.AvailableModelIds[index], session.ActiveModelId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private static string CreateCurrentSettingsSummary(ReplCommandContext context)
    {
        return "Current settings: " +
            $"provider {context.Session.ProviderName}, " +
            $"model {context.Session.ActiveModelId}, " +
            $"profile {context.Session.AgentProfile.Name}, " +
            $"thinking {ReasoningEffortOptions.Format(context.Session.ReasoningEffort)}.";
    }

    private static string FormatHelp()
    {
        return "Settings commands:\n" +
            "/setting - Open the settings picker.\n" +
            "/setting model [id] - Pick the active model, or switch directly when an id is supplied.\n" +
            "/setting profile [name] - Pick the active profile, or switch directly when a name is supplied.\n" +
            "/setting thinking [on|off] - Pick or set thinking mode.\n" +
            "/setting provider [list|name] - List or switch saved providers.\n" +
            "/setting onboarding - Re-run provider onboarding and add a saved provider.\n" +
            "/setting budget [status|local|cloud] - Show or configure budget controls.\n" +
            "/setting workspace [recommended|minimal|custom] - Choose and initialize workspace-local NanoAgent files.\n" +
            "/setting permissions - Open permission settings for modes, sandbox, and session overrides.\n" +
            "/setting rules - Inspect effective permission rules in a picker.\n" +
            "/setting tools - Show MCP servers, custom tools, and dynamic tool status.\n" +
            "/setting summary - Review provider, session, profile, thinking, and model details.";
    }

    private static string FormatPermissionMode(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Allow => "Allow",
            PermissionMode.Ask => "Ask",
            PermissionMode.Deny => "Deny",
            _ => mode.ToString()
        };
    }

    private static string FormatSandboxMode(ToolSandboxMode mode)
    {
        return mode switch
        {
            ToolSandboxMode.ReadOnly => "Read only",
            ToolSandboxMode.WorkspaceWrite => "Workspace write",
            ToolSandboxMode.DangerFullAccess => "Danger full access",
            _ => mode.ToString()
        };
    }

    private enum SettingCommandArea
    {
        Model,
        Profile,
        Thinking,
        Provider,
        Budget,
        Workspace,
        Permissions,
        Tools,
        Summary
    }

    private enum SettingPermissionAction
    {
        DefaultMode,
        SandboxMode,
        Edits,
        FileReads,
        Shell,
        Network,
        MemoryWrite,
        McpTools,
        AddAllow,
        AddDeny,
        SessionOverrides,
        Rules,
        Back
    }

    private enum SettingPermissionModeAction
    {
        Allow,
        Ask,
        Deny,
        Back
    }

    private enum SettingSandboxModeAction
    {
        ReadOnly,
        WorkspaceWrite,
        DangerFullAccess,
        Back
    }

    private enum SettingSessionOverrideAction
    {
        Back,
        Clear
    }

    private enum SettingRulesAction
    {
        Back
    }

    private enum SettingSummaryAction
    {
        Back
    }

    private enum SettingToolsAction
    {
        DynamicTools,
        McpPermission,
        Back
    }

    private enum SettingDynamicToolsAction
    {
        Back
    }
}
