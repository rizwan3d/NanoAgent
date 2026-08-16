using FluentAssertions;
using Moq;
using System.Reflection;
using StemCode.Application.Backend;
using StemCode.Application.Voice;
using StemCode.CLI;

namespace StemCode.Tests.CLI;

public sealed class VoiceCommandTests
{
    [Fact]
    public void IsVoiceDictationKey_Should_MatchCtrlRAndRawControlCharacter()
    {
        ConsoleKeyInfo ctrlR = new('r', ConsoleKey.R, shift: false, alt: false, control: true);
        ConsoleKeyInfo rawCtrlR = new('\u0012', ConsoleKey.R, shift: false, alt: false, control: false);
        ConsoleKeyInfo plainR = new('r', ConsoleKey.R, shift: false, alt: false, control: false);

        Program.IsVoiceDictationKey(ctrlR).Should().BeTrue();
        Program.IsVoiceDictationKey(rawCtrlR).Should().BeTrue();
        Program.IsVoiceDictationKey(plainR).Should().BeFalse();
    }

    [Fact]
    public void GetDefaultVoiceModelIndex_Should_PreferRecommendedModelOnFirstUse()
    {
        VoiceModelOption[] models =
        [
            new("fast", "Fast", "Fast"),
            new("balanced", "Balanced", "Balanced", IsRecommended: true),
            new("accurate", "Accurate", "Accurate")
        ];

        int selectedIndex = Program.GetDefaultVoiceModelIndex(models, settings: null);

        selectedIndex.Should().Be(1);
    }

    [Fact]
    public void GetDefaultVoiceModelIndex_Should_PreferSavedModelDuringSetup()
    {
        VoiceModelOption[] models =
        [
            new("fast", "Fast", "Fast"),
            new("balanced", "Balanced", "Balanced", IsRecommended: true),
            new("accurate", "Accurate", "Accurate")
        ];

        int selectedIndex = Program.GetDefaultVoiceModelIndex(
            models,
            new VoiceSettings("accurate", null));

        selectedIndex.Should().Be(2);
    }

    [Fact]
    public async Task VoiceSetupCommand_Should_ClearInputAndOpenModelSelectionWithRecommendedDefault()
    {
        AppState state = CreateReadyState();
        FakeVoiceDictationService service = new();
        VoiceInteractionState.For(state).Service = service;
        state.Input.Append("/voice setup");
        state.InputCursorIndex = state.Input.Length;

        bool handled = Program.TryHandleVoiceInputCommand(state);
        Task operation = VoiceInteractionState.For(state).Operation!;
        await operation;
        state.UiBridge.ApplyPending(state);

        handled.Should().BeTrue();
        state.Input.ToString().Should().BeEmpty();
        state.ActiveModal.Should().BeOfType<SelectionModalState<VoiceModelOption>>();
        ((SelectionModalState<VoiceModelOption>)state.ActiveModal!).SelectedIndex.Should().Be(1);
    }

    [Fact]
    public async Task VoiceUpdateCommand_Should_UpdateModelsWithoutForwardingCommand()
    {
        AppState state = CreateReadyState();
        FakeVoiceDictationService service = new();
        VoiceInteractionState.For(state).Service = service;
        state.Input.Append("/voice update");
        state.InputCursorIndex = state.Input.Length;

        bool handled = Program.TryHandleVoiceInputCommand(state);
        Task operation = VoiceInteractionState.For(state).Operation!;
        await operation;
        state.UiBridge.ApplyPending(state);

        handled.Should().BeTrue();
        service.UpdateCount.Should().Be(1);
        state.Input.ToString().Should().BeEmpty();
        state.Messages.Should().ContainSingle(message =>
            message.Text == "Voice models are up to date.");
    }

    private static AppState CreateReadyState()
    {
        return new AppState(
            new UiBridge(),
            new Mock<IStemCodeBackend>(MockBehavior.Strict).Object)
        {
            IsReady = true,
            ActivityText = "Ready"
        };
    }

    [Fact]
    public void VoiceCommand_Should_AppearInSlashCommandAutocomplete()
    {
        MethodInfo getSuggestions = typeof(Program).GetMethod(
            "GetSlashCommandSuggestions",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            [typeof(string)],
            null)!;
        getSuggestions.Should().NotBeNull();

        object? result = getSuggestions!.Invoke(null, [Directory.GetCurrentDirectory()]);
        result.Should().NotBeNull();

        System.Collections.IEnumerable suggestions = (System.Collections.IEnumerable)result!;
        List<string> commands = new();
        foreach (object item in suggestions)
        {
            commands.Add((string)item.GetType().GetProperty("Command")!.GetValue(item)!);
        }

        commands.Should().Contain("/voice");
        commands.Should().Contain("/voice setup");
        commands.Should().Contain("/voice update");
    }

    [Fact]
    public void StopVoiceOperation_Should_CancelActiveVoiceOperation()
    {
        AppState state = CreateReadyState();
        VoiceInteractionState voice = VoiceInteractionState.For(state);
        voice.IsBusy = true;
        voice.Cancellation = new CancellationTokenSource();

        Program.StopVoiceOperation(state);

        voice.Cancellation.IsCancellationRequested.Should().BeTrue();
    }

    private sealed class FakeVoiceDictationService : IVoiceDictationService
    {
        public int UpdateCount { get; private set; }

        public Task<VoiceSettings?> LoadSettingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<VoiceSettings?>(null);
        }

        public Task SaveSettingsAsync(VoiceSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VoiceModelOption>> GetModelsAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<VoiceModelOption> models =
            [
                new VoiceModelOption("fast", "Fast", "Fast"),
                new VoiceModelOption("balanced", "Balanced", "Balanced", IsRecommended: true),
                new VoiceModelOption("accurate", "Accurate", "Accurate")
            ];
            return Task.FromResult(models);
        }

        public Task<IReadOnlyList<VoiceInputDevice>> GetInputDevicesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<VoiceInputDevice> devices =
            [new VoiceInputDevice(string.Empty, "System default", IsDefault: true)];
            return Task.FromResult(devices);
        }

        public Task EnsureModelAsync(
            string modelId,
            IProgress<VoiceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> DictateAsync(
            VoiceSettings settings,
            IProgress<VoiceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("voice transcript");
        }

        public Task UpdateModelsAsync(
            IProgress<VoiceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }
}
