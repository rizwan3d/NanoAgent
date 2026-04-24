using System.Text;
using NanoAgent.Application.Models;
using Spectre.Console;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string DefaultCompletionNote = "(0s · 0 tokens)";
    private const double EstimatedLiveTokensPerSecond = 4d;
    private const int HeaderDividerWidth = 53;
    private const int HeaderPanelSize = 10;
    private const int InputCursorBlinkIntervalMilliseconds = 500;
    private const int InputCursorColumnWidth = 1;
    private const int MessageScrollbarColumnWidth = 2;
    private const int MouseWheelScrollLineCount = 3;
    private const int TerminalSequenceReadTimeoutMilliseconds = 25;
    private const string RepositoryUrl = "github.com/rizwan3d/NanoAgent";
    private const string EnableAlternateScreenSequence = "\u001b[?1049h";
    private const string DisableAlternateScreenSequence = "\u001b[?1049l";
    private const string EnableWheelScrollingSequence = "\u001b[?1007h";
    private const string DisableWheelScrollingSequence = "\u001b[?1007l";
    private const string DisableMouseTrackingSequence = "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l";
    private const string SponsorName = "ALFAIN Technologies (PVT) Limited";
    private const string SponsorUrl = "https://alfain.co/";
    private const int StdInputHandle = -10;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private static uint? s_originalInputMode;
    private static readonly string[] Spinner =
    [
        "-",
        "\\",
        "|",
        "/"
    ];

    public static async Task Main(string[]? args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;
        EnableTerminalWheelScrolling();

        UiBridge uiBridge = new();
        NanoCliBackend backend = new(args ?? []);
        AppState state = new(uiBridge, backend);

        StartInitialization(state);

        try
        {
            await AnsiConsole
                .Live(BuildUi(state))
                .StartAsync(async context =>
                {
                    while (state.Running)
                    {
                        state.UiBridge.ApplyPending(state);
                        HandleInput(state);
                        UpdateModal(state);
                        UpdateStreaming(state);

                        context.UpdateTarget(BuildUi(state));
                        context.Refresh();

                        await Task.Delay(16);
                    }
                });
        }
        finally
        {
            state.LifetimeCancellation.Cancel();

            try
            {
                await backend.DisposeAsync();
            }
            finally
            {
                AnsiConsole.Clear();
                DisableTerminalWheelScrolling();
                state.LifetimeCancellation.Dispose();
                Console.CursorVisible = true;
                Console.ResetColor();
            }
        }
    }

    private static void StartInitialization(AppState state)
    {
        state.IsBusy = true;
        state.ActivityText = "Loading NanoAgent services";

        state.ActiveOperation = Task.Run(async () =>
        {
            try
            {
                BackendSessionInfo sessionInfo = await state.Backend.InitializeAsync(
                    state.UiBridge,
                    state.LifetimeCancellation.Token);

                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.IsReady = true;
                    appState.HasFatalError = false;
                    appState.ActivityText = "Ready";
                    appState.ProviderName = sessionInfo.ProviderName;
                    appState.ActiveModelId = sessionInfo.ModelId;
                });
            }
            catch (OperationCanceledException) when (state.LifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.HasFatalError = true;
                    appState.ActivityText = "Backend startup failed";
                    appState.AddSystemMessage($"Failed to start NanoAgent: {exception.Message}");
                });
            }
        });
    }
}
