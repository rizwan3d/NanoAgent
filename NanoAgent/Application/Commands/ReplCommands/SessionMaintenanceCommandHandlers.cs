using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using System.Globalization;

namespace NanoAgent.Application.Commands;

internal sealed class CompactCommandHandler : IReplCommandHandler
{
    public string CommandName => "compact";

    public string Description => "Manually compact the session context.";

    public string Usage => "/compact [retained-turns]";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        int retainedTurns = SessionCommandSupport.DefaultCompactRetainedTurns;
        if (context.Arguments.Count > 0 &&
            (!int.TryParse(context.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out retainedTurns) ||
             retainedTurns < 0))
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Retained turns must be zero or greater. Usage: /compact [retained-turns]",
                ReplFeedbackKind.Error));
        }

        int compactedTurns = context.Session.CompactConversationHistory(retainedTurns);
        if (compactedTurns == 0)
        {
            return Task.FromResult(ReplCommandResult.Continue(
                "Session is already compact enough.",
                ReplFeedbackKind.Warning));
        }

        return Task.FromResult(ReplCommandResult.SwitchSession(
            context.Session,
            $"Compacted {compactedTurns.ToString(CultureInfo.InvariantCulture)} older turn(s). Retained {retainedTurns.ToString(CultureInfo.InvariantCulture)} recent turn(s).",
            replaySession: true));
    }
}

internal sealed class ReloadCommandHandler : IReplCommandHandler
{
    private readonly IEnumerable<IDynamicToolProvider> _dynamicToolProviders;
    private readonly IAgentProfileResolver _profileResolver;
    private readonly ISkillService _skillService;
    private readonly IToolRegistry _toolRegistry;
    private readonly IWorkspaceAgentProfilePromptProvider _workspaceAgentProfilePromptProvider;
    private readonly IWorkspaceSystemPromptProvider _workspaceSystemPromptProvider;

    public ReloadCommandHandler(
        IEnumerable<IDynamicToolProvider> dynamicToolProviders,
        IAgentProfileResolver profileResolver,
        ISkillService skillService,
        IToolRegistry toolRegistry,
        IWorkspaceAgentProfilePromptProvider workspaceAgentProfilePromptProvider,
        IWorkspaceSystemPromptProvider workspaceSystemPromptProvider)
    {
        _dynamicToolProviders = dynamicToolProviders;
        _profileResolver = profileResolver;
        _skillService = skillService;
        _toolRegistry = toolRegistry;
        _workspaceAgentProfilePromptProvider = workspaceAgentProfilePromptProvider;
        _workspaceSystemPromptProvider = workspaceSystemPromptProvider;
    }

    public string CommandName => "reload";

    public string Description => "Reload keybindings, extensions, skills, prompts, and themes.";

    public string Usage => "/reload";

    public async Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        int profileCount = _profileResolver.List().Count;
        int skillCount = (await _skillService.ListAsync(context.Session, cancellationToken)).Count;
        bool hasWorkspaceSystemPrompt = !string.IsNullOrWhiteSpace(
            await _workspaceSystemPromptProvider.LoadAsync(context.Session, cancellationToken));
        bool hasWorkspaceAgentPrompt = !string.IsNullOrWhiteSpace(
            await _workspaceAgentProfilePromptProvider.LoadAsync(context.Session, cancellationToken));
        DynamicToolProviderStatus[] dynamicStatuses = _dynamicToolProviders
            .SelectMany(static provider => provider.GetStatuses())
            .ToArray();
        int dynamicToolCount = dynamicStatuses.Sum(static status => status.ToolCount);
        int registeredToolCount = _toolRegistry.GetRegisteredToolNames().Count;

        string message =
            "Reload complete:\n" +
            $"Profiles: {profileCount.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Skills: {skillCount.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Workspace prompts: {FormatPromptStatus(hasWorkspaceSystemPrompt, hasWorkspaceAgentPrompt)}\n" +
            $"Dynamic tools: {dynamicToolCount.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Registered tools: {registeredToolCount.ToString(CultureInfo.InvariantCulture)}\n" +
            "Keybindings, extensions, and themes were refreshed where supported by the active terminal UI.";

        return ReplCommandResult.Continue(message);
    }

    private static string FormatPromptStatus(
        bool hasWorkspaceSystemPrompt,
        bool hasWorkspaceAgentPrompt)
    {
        if (hasWorkspaceSystemPrompt && hasWorkspaceAgentPrompt)
        {
            return "system and profile";
        }

        if (hasWorkspaceSystemPrompt)
        {
            return "system";
        }

        return hasWorkspaceAgentPrompt
            ? "profile"
            : "none";
    }
}
