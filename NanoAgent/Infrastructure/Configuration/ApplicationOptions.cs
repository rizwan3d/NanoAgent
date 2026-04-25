using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Configuration;

public sealed class ApplicationOptions
{
    public const string SectionName = "Application";

    public ConversationOptions Conversation { get; set; } = new();

    public ApplicationDefaultsOptions Defaults { get; set; } = new();

    public LifecycleHookSettings Hooks { get; set; } = new();

    public ModelSelectionOptions ModelSelection { get; set; } = new();

    public PermissionSettings Permissions { get; set; } = new();
}
