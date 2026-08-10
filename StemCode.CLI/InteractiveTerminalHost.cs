using StemCode.Application.Backend;
using StemCode.Application.Models;
using Spectre.Console;

namespace StemCode.CLI;

internal static class InteractiveTerminalHost
{
    public static async Task<int> RunAsync(
        BackendRuntimeArguments runtimeArguments,
        string? providerAuthKey,
        bool noOldReader,
        bool autoApproveAllTools)
    {
        ConsoleCancelEventHandler? cancelKeyPressHandler = null;
        IStemCodeBackend? backend = null;
        AppState? state = null;
        TerminalSession? terminal = null;

        try
        {
            terminal = TerminalSession.EnterInteractiveMode();

            UiBridge uiBridge = new(providerAuthKey);
            BackendRuntimeArguments interactiveRuntimeArguments = BackendRuntimeArguments.Parse(
                    Program.EnsureStartupPromptsArg(runtimeArguments.RawArgs, enabled: true))
                .WithDefaults(
                    runtimeArguments.EffectiveAppSurface(BackendRuntimeOptions.CliSurface),
                    runtimeArguments.SkipUpdateCheck);
            backend = new StemCodeBackend(
                interactiveRuntimeArguments,
                sessionMcpServers: [],
                autoApproveAllTools);
            state = new AppState(uiBridge, backend);
            cancelKeyPressHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                state.Running = false;
            };

            Console.CancelKeyPress += cancelKeyPressHandler;
            Program.StartInitialization(state, noOldReader);

            await AnsiConsole
                .Live(Program.BuildUi(state))
                .StartAsync(async context =>
                {
                    while (state.Running)
                    {
                        state.UiBridge.ApplyPending(state);
                        Program.HandleInput(state);
                        UpdateModal(state);
                        Program.UpdateStreaming(state);

                        if (!state.IsReaderViewActive || state.ReaderViewDirty)
                        {
                            context.UpdateTarget(Program.BuildUi(state));
                            context.Refresh();
                            state.ReaderViewDirty = false;
                        }

                        await Task.Delay(16);
                    }
                });

            return ExitCodeMapper.Success;
        }
        finally
        {
            if (cancelKeyPressHandler is not null)
            {
                Console.CancelKeyPress -= cancelKeyPressHandler;
            }

            state?.LifetimeCancellation.Cancel();

            try
            {
                if (backend is not null)
                {
                    await backend.DisposeAsync();
                }
            }
            finally
            {
                terminal?.Dispose();
                AnsiConsole.Clear();
                if (state is not null)
                {
                    state.LifetimeCancellation.Dispose();
                    Program.WriteFatalExitMessage(state);
                    Program.WriteExitResumeHint(state);
                }
            }
        }
    }

    private static void UpdateModal(AppState state)
    {
        state.ActiveModal?.Update(state);
    }
}
