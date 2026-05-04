using FluentAssertions;
using Moq;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Profiles;
using NanoAgent.Application.Services;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Application.Commands;

public sealed class SettingCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Should_OpenSettingsPickerAndPickModel()
    {
        QueueSelectionPrompt selectionPrompt = new("Model", "model-b");
        HandlerServiceProvider serviceProvider = new();
        CapturingConfigurationStore configurationStore = new();
        SettingCommandHandler sut = CreateHandler(
            selectionPrompt,
            serviceProvider,
            configurationStore);
        serviceProvider.Handlers = [sut];
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session),
            CancellationToken.None);

        result.Message.Should().BeNull();
        session.ActiveModelId.Should().Be("model-b");
        configurationStore.SavedConfiguration.Should().NotBeNull();
        configurationStore.SavedConfiguration!.PreferredModelId.Should().Be("model-b");
        selectionPrompt.RequestTitles.Should().Equal(
            "NanoAgent settings",
            "Choose active model",
            "NanoAgent settings");
    }

    [Fact]
    public async Task ExecuteAsync_Should_PickProfileFromSettingsPicker()
    {
        QueueSelectionPrompt selectionPrompt = new("Profile", "plan");
        HandlerServiceProvider serviceProvider = new();
        SettingCommandHandler sut = CreateHandler(selectionPrompt, serviceProvider);
        serviceProvider.Handlers = [sut];
        ReplSessionContext session = CreateSession();

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session),
            CancellationToken.None);

        result.Message.Should().BeNull();
        session.AgentProfile.Name.Should().Be("plan");
        selectionPrompt.RequestTitles.Should().Equal(
            "NanoAgent settings",
            "Choose active profile",
            "NanoAgent settings");
    }

    [Fact]
    public async Task ExecuteAsync_Should_PickThinkingModeAndSaveConfiguration()
    {
        QueueSelectionPrompt selectionPrompt = new("Thinking", "On");
        HandlerServiceProvider serviceProvider = new();
        AgentProviderProfile providerProfile = new(ProviderKind.OpenAi, null);
        ReplSessionContext session = new(
            providerProfile,
            "model-a",
            ["model-a"],
            reasoningEffort: "off");
        Mock<IAgentConfigurationStore> configurationStore = new(MockBehavior.Strict);
        configurationStore
            .Setup(store => store.SaveAsync(
                new AgentConfiguration(providerProfile, "model-a", "on"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SettingCommandHandler sut = new(
            selectionPrompt,
            new BuiltInAgentProfileResolver(),
            new ModelActivationService(),
            configurationStore.Object,
            new PermissionSettings(),
            serviceProvider,
            new ThrowingTextPrompt(),
            [],
            new EmptyToolRegistry());
        serviceProvider.Handlers = [sut];

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(session),
            CancellationToken.None);

        result.Message.Should().BeNull();
        session.ReasoningEffort.Should().Be("on");
        selectionPrompt.RequestTitles.Should().Equal(
            "NanoAgent settings",
            "Choose thinking mode",
            "NanoAgent settings");
        configurationStore.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Should_EditPermissionsInPickerWithoutPrintingSummary()
    {
        QueueSelectionPrompt selectionPrompt = new("Sandbox mode", "Read only", "Back");
        HandlerServiceProvider serviceProvider = new();
        CapturingHandler permissionsHandler = new("permissions", ReplCommandResult.Continue("Permissions summary."));
        PermissionSettings permissionSettings = new()
        {
            SandboxMode = ToolSandboxMode.WorkspaceWrite
        };
        SettingCommandHandler sut = CreateHandler(
            selectionPrompt,
            serviceProvider,
            permissionSettings: permissionSettings);
        serviceProvider.Handlers = [sut, permissionsHandler];

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(CreateSession(), "permissions"),
            CancellationToken.None);

        result.Message.Should().BeNull();
        permissionSettings.SandboxMode.Should().Be(ToolSandboxMode.ReadOnly);
        permissionsHandler.LastContext.Should().BeNull();
        selectionPrompt.RequestTitles.Should().Equal(
            "Permission settings",
            "Sandbox mode",
            "Permission settings");
    }

    [Fact]
    public async Task ExecuteAsync_Should_DelegateBudgetAreaWithRemainingArguments()
    {
        QueueSelectionPrompt selectionPrompt = new();
        HandlerServiceProvider serviceProvider = new();
        CapturingHandler budgetHandler = new("budget", ReplCommandResult.Continue("Budget status."));
        SettingCommandHandler sut = CreateHandler(selectionPrompt, serviceProvider);
        serviceProvider.Handlers = [sut, budgetHandler];

        ReplCommandResult result = await sut.ExecuteAsync(
            CreateContext(CreateSession(), "budget status"),
            CancellationToken.None);

        result.Message.Should().Be("Budget status.");
        budgetHandler.LastContext.Should().NotBeNull();
        budgetHandler.LastContext!.CommandName.Should().Be("budget");
        budgetHandler.LastContext.ArgumentText.Should().Be("status");
        budgetHandler.LastContext.Arguments.Should().Equal("status");
        budgetHandler.LastContext.RawText.Should().Be("/budget status");
        selectionPrompt.RequestTitles.Should().BeEmpty();
    }

    private static SettingCommandHandler CreateHandler(
        QueueSelectionPrompt selectionPrompt,
        HandlerServiceProvider serviceProvider,
        IAgentConfigurationStore? configurationStore = null,
        PermissionSettings? permissionSettings = null)
    {
        return new SettingCommandHandler(
            selectionPrompt,
            new BuiltInAgentProfileResolver(),
            new ModelActivationService(),
            configurationStore ?? new CapturingConfigurationStore(),
            permissionSettings ?? new PermissionSettings(),
            serviceProvider,
            new ThrowingTextPrompt(),
            [],
            new EmptyToolRegistry());
    }

    private static ReplCommandContext CreateContext(
        ReplSessionContext session,
        string argumentText = "")
    {
        string[] arguments = string.IsNullOrWhiteSpace(argumentText)
            ? []
            : argumentText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ReplCommandContext(
            "setting",
            argumentText,
            arguments,
            string.IsNullOrWhiteSpace(argumentText) ? "/setting" : $"/setting {argumentText}",
            session);
    }

    private static ReplSessionContext CreateSession()
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "model-a",
            ["model-a", "model-b"]);
    }

    private sealed class QueueSelectionPrompt : ISelectionPrompt
    {
        private readonly Queue<string> _labels;

        public QueueSelectionPrompt(params string[] labels)
        {
            _labels = new Queue<string>(labels);
        }

        public List<string> RequestTitles { get; } = [];

        public Task<T> PromptAsync<T>(
            SelectionPromptRequest<T> request,
            CancellationToken cancellationToken)
        {
            RequestTitles.Add(request.Title);
            if (!_labels.TryDequeue(out string? label))
            {
                throw new PromptCancelledException();
            }

            SelectionPromptOption<T> option = request.Options.Single(candidate =>
                string.Equals(candidate.Label, label, StringComparison.Ordinal));
            return Task.FromResult(option.Value);
        }
    }

    private sealed class CapturingConfigurationStore : IAgentConfigurationStore
    {
        public AgentConfiguration? SavedConfiguration { get; private set; }

        public Task<AgentConfiguration?> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<AgentConfiguration?>(null);
        }

        public Task SaveAsync(
            AgentConfiguration configuration,
            CancellationToken cancellationToken)
        {
            SavedConfiguration = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTextPrompt : ITextPrompt
    {
        public Task<string> PromptAsync(
            TextPromptRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected text prompt.");
        }
    }

    private sealed class EmptyToolRegistry : IToolRegistry
    {
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            return [];
        }

        public IReadOnlyList<string> GetRegisteredToolNames()
        {
            return [];
        }

        public bool TryResolve(string toolName, out ToolRegistration? tool)
        {
            tool = null;
            return false;
        }
    }

    private sealed class HandlerServiceProvider : IServiceProvider
    {
        public IReadOnlyList<IReplCommandHandler> Handlers { get; set; } = [];

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IEnumerable<IReplCommandHandler>)
                ? Handlers
                : null;
        }
    }

    private sealed class CapturingHandler : IReplCommandHandler
    {
        private readonly ReplCommandResult _result;

        public CapturingHandler(
            string commandName,
            ReplCommandResult result)
        {
            CommandName = commandName;
            _result = result;
        }

        public string CommandName { get; }

        public string Description => CommandName;

        public string Usage => "/" + CommandName;

        public ReplCommandContext? LastContext { get; private set; }

        public Task<ReplCommandResult> ExecuteAsync(
            ReplCommandContext context,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(_result);
        }
    }
}
