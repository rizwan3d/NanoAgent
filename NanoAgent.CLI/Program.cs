using NanoAgent.Application.Backend;
using NanoAgent.Application.Commands;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.WindowsSandbox;
using NanoAgent.Infrastructure.Telemetry;
using Spectre.Console;
using System.Text;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const double EstimatedLiveTokensPerSecond = 4d;
    private const int InputCursorBlinkIntervalMilliseconds = 500;
    private const int InputCursorColumnWidth = 1;
    private const int MessageScrollbarColumnWidth = 2;
    private const int MouseWheelScrollLineCount = 3;
    private const int MultilinePastePreviewLineThreshold = 3;
    private const int PasteContinuationReadTimeoutMilliseconds = 40;
    private const int ClipboardReadTimeoutMilliseconds = 2000;
    private const int MaxSlashCommandSuggestionCount = 8;
    private const int TerminalSequenceReadTimeoutMilliseconds = 25;
    private const string EnableAlternateScreenSequence = "\u001b[?1049h";
    private const string DisableAlternateScreenSequence = "\u001b[?1049l";
    private const string EnableBracketedPasteSequence = "\u001b[?2004h";
    private const string DisableBracketedPasteSequence = "\u001b[?2004l";
    private const string DisableWheelScrollingSequence = "\u001b[?1007l";
    // Normal button tracking (?1000h) plus SGR extended coordinates (?1006h): reports
    // clicks and wheel events with row/column so we can hit-test the conversation. This
    // captures the mouse, so native drag-select needs Shift+drag (or Reader View / Copy
    // mode) -- an accepted trade-off for click-to-toggle.
    private const string EnableMouseTrackingSequence = "\u001b[?1000h\u001b[?1006h";
    private const string DisableMouseTrackingSequence = "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l";
    private const int StdInputHandle = -10;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private static uint? s_originalInputMode;
    private static readonly string[] Spinner =
    [
        "⠋",
        "⠙",
        "⠹",
        "⠸",
        "⠼",
        "⠴",
        "⠦",
        "⠧",
        "⠇",
        "⠏"
    ];
    public static async Task<int> Main(string[]? args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string[] effectiveArgs = args ?? [];
        if (TryHandleWindowsSandboxSpecialInvocation(effectiveArgs, out int specialExitCode))
        {
            return specialExitCode;
        }

        CliInvocation invocation;
        try
        {
            invocation = CliInvocation.Parse(
                effectiveArgs,
                Console.IsInputRedirected,
                Console.In.ReadToEnd);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            WriteUsage(Console.Error);
            return 2;
        }

        if (invocation.ShowHelp)
        {
            WriteUsage(Console.Out);
            return 0;
        }

        if (invocation.ShowVersion)
        {
            Console.Out.WriteLine(GetVersionText());
            return 0;
        }

        if (invocation.Mode == CliMode.SingleTurn)
        {
            return await RunSingleTurnAsync(
                invocation.RuntimeArguments.WithDefaults(
                    BackendRuntimeOptions.CliSurface),
                invocation.ProviderAuthKey,
                invocation.Prompt ?? string.Empty,
                invocation.JsonOutput,
                invocation.AutoApproveAllTools);
        }

        if (invocation.Mode == CliMode.Acp)
        {
            return await RunAcpAsync(
                invocation.RuntimeArguments.WithDefaults(BackendRuntimeOptions.CliSurface).RawArgs,
                invocation.ProviderAuthKey,
                invocation.NoOldReader,
                invocation.AutoApproveAllTools);
        }

        await RunInteractiveAsync(
            invocation.RuntimeArguments.WithDefaults(BackendRuntimeOptions.CliSurface),
            invocation.ProviderAuthKey,
            invocation.NoOldReader,
            invocation.AutoApproveAllTools);
        return 0;
    }

    internal static bool TryHandleWindowsSandboxSpecialInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        if (TryHandleWindowsSandboxSetupInvocation(args, out exitCode))
        {
            return true;
        }

        return TryHandleWindowsSandboxRunnerInvocation(args, out exitCode);
    }

    private static bool TryHandleWindowsSandboxSetupInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        exitCode = 0;

        int commandIndex = -1;
        for (int index = 0; index < args.Count; index++)
        {
            if (string.Equals(
                    args[index],
                    WindowsSandboxSetupOrchestrator.SetupCommandArgument,
                    StringComparison.Ordinal))
            {
                commandIndex = index;
                break;
            }
        }

        if (commandIndex < 0)
        {
            return false;
        }

        int payloadIndex = commandIndex + 1;
        if (payloadIndex >= args.Count || string.IsNullOrWhiteSpace(args[payloadIndex]))
        {
            Console.Error.WriteLine("Missing setup payload for Windows sandbox setup mode.");
            exitCode = 2;
            return true;
        }

        exitCode = WindowsSandboxSetupOrchestrator.RunEncodedSetupPayload(args[payloadIndex]);
        return true;
    }

    private static bool TryHandleWindowsSandboxRunnerInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        exitCode = 0;

        if (!args.Any(arg => string.Equals(
                arg,
                WindowsSandboxProcessRunner.RunnerCommandArgument,
                StringComparison.Ordinal)))
        {
            return false;
        }

        if (!TryReadRunnerPipeArgument(args, "--pipe-in", out string? pipeIn) ||
            !TryReadRunnerPipeArgument(args, "--pipe-out", out string? pipeOut))
        {
            Console.Error.WriteLine("Missing required pipe arguments for Windows sandbox runner mode.");
            exitCode = 2;
            return true;
        }

        exitCode = WindowsSandboxProcessRunner.RunPipeRunner(
            WindowsSandboxRunnerClient.ParsePipeArgument(pipeIn!),
            WindowsSandboxRunnerClient.ParsePipeArgument(pipeOut!));
        return true;
    }

    private static bool TryReadRunnerPipeArgument(
        IReadOnlyList<string> args,
        string optionName,
        out string? value)
    {
        value = null;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                int valueIndex = index + 1;
                if (valueIndex >= args.Count || string.IsNullOrWhiteSpace(args[valueIndex]))
                {
                    return false;
                }

                value = args[valueIndex].Trim();
                return true;
            }

            string prefix = optionName + "=";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string candidate = arg[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            value = candidate;
            return true;
        }

        return false;
    }

    private static async Task<int> RunAcpAsync(
        string[] args,
        string? providerAuthKey,
        bool noOldReader,
        bool autoApproveAllTools)
    {
        AcpServer server = new(
            Console.In,
            Console.Out,
            Console.Error,
            args,
            providerAuthKey,
            noOldReader,
            autoApproveAllTools);

        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;

        try
        {
            await server.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NanoAgent ACP error: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
            await server.DisposeAsync();
        }
    }

    private static async Task RunInteractiveAsync(
        BackendRuntimeArguments runtimeArguments,
        string? providerAuthKey,
        bool noOldReader,
        bool autoApproveAllTools)
    {
        ConsoleCancelEventHandler? cancelKeyPressHandler = null;
        INanoAgentBackend? backend = null;
        AppState? state = null;

        try
        {
            Console.CursorVisible = false;
            EnableTerminalWheelScrolling();

            UiBridge uiBridge = new(providerAuthKey);
            BackendRuntimeArguments interactiveRuntimeArguments = BackendRuntimeArguments.Parse(
                    EnsureStartupPromptsArg(runtimeArguments.RawArgs, enabled: true))
                .WithDefaults(
                    runtimeArguments.EffectiveAppSurface(BackendRuntimeOptions.CliSurface),
                    runtimeArguments.SkipUpdateCheck);
            backend = new NanoAgentBackend(
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
            StartInitialization(state, noOldReader);

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

                        // While the reader view is open we leave the screen untouched
                        // unless it has been marked dirty (entered or scrolled), so a
                        // native terminal text selection is not wiped by the next frame.
                        if (!state.IsReaderViewActive || state.ReaderViewDirty)
                        {
                            context.UpdateTarget(BuildUi(state));
                            context.Refresh();
                            state.ReaderViewDirty = false;
                        }

                        await Task.Delay(16);
                    }
                });
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
                AnsiConsole.Clear();
                DisableTerminalWheelScrolling();
                Console.CursorVisible = true;
                Console.ResetColor();
                if (state is not null)
                {
                    state.LifetimeCancellation.Dispose();
                    WriteFatalExitMessage(state);
                    WriteExitResumeHint(state);
                }
            }
        }
    }

    private static async Task<int> RunSingleTurnAsync(
        BackendRuntimeArguments runtimeArguments,
        string? providerAuthKey,
        string prompt,
        bool jsonOutput,
        bool autoApproveAllTools)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            WriteSingleTurnError(jsonOutput, "missing_prompt", "No prompt was provided.");
            return 2;
        }

        ConsoleBridge uiBridge = new(providerAuthKey);
        await using INanoAgentBackend backend = new NanoAgentBackend(
            runtimeArguments,
            sessionMcpServers: [],
            autoApproveAllTools);
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;

        try
        {
            BackendSessionInfo sessionInfo = await backend.InitializeAsync(uiBridge, cancellation.Token);

            string normalizedPrompt = prompt.Trim();
            if (normalizedPrompt.StartsWith("/", StringComparison.Ordinal))
            {
                if (CustomSlashCommandService.TryExpand(
                        Directory.GetCurrentDirectory(),
                        normalizedPrompt,
                        out CustomSlashCommandResolution? customCommand,
                        out string? customCommandError))
                {
                    if (customCommand is null)
                    {
                        WriteSingleTurnError(
                            jsonOutput,
                            "custom_command_error",
                            customCommandError ?? "Custom command could not be expanded.");
                        return 1;
                    }

                    ConversationTurnResult customResult = await backend.RunTurnAsync(
                        customCommand.ExpandedPrompt,
                        uiBridge,
                        cancellation.Token);

                    Console.WriteLine(jsonOutput
                        ? CliJsonOutputWriter.FormatTurn(customResult, sessionInfo)
                        : customResult.ResponseText);
                    return 0;
                }

                BackendCommandResult commandResult = await backend.RunCommandAsync(
                    normalizedPrompt,
                    cancellation.Token);

                if (jsonOutput)
                {
                    Console.WriteLine(CliJsonOutputWriter.FormatCommand(commandResult));
                }
                else
                {
                    WriteCommandResult(commandResult.CommandResult);
                }

                return commandResult.CommandResult.FeedbackKind == ReplFeedbackKind.Error ? 1 : 0;
            }

            ConversationTurnResult result = await backend.RunTurnAsync(
                normalizedPrompt,
                uiBridge,
                cancellation.Token);

            Console.WriteLine(jsonOutput
                ? CliJsonOutputWriter.FormatTurn(result, sessionInfo)
                : result.ResponseText);
            return 0;
        }
        catch (PromptCancelledException exception)
        {
            WriteSingleTurnError(jsonOutput, "prompt_cancelled", exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            WriteSingleTurnError(jsonOutput, "cancelled", "Cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            WriteSingleTurnError(jsonOutput, "error", exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
        }
    }

    private static void WriteSingleTurnError(
        bool jsonOutput,
        string errorCode,
        string message)
    {
        if (jsonOutput)
        {
            Console.WriteLine(CliJsonOutputWriter.FormatError(errorCode, message));
            return;
        }

        Console.Error.WriteLine(errorCode == "error"
            ? $"NanoAgent error: {message}"
            : message);
    }

    private static void WriteCommandResult(ReplCommandResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return;
        }

        TextWriter writer = result.FeedbackKind == ReplFeedbackKind.Info
            ? Console.Out
            : Console.Error;

        string prefix = result.FeedbackKind switch
        {
            ReplFeedbackKind.Error => "Error: ",
            ReplFeedbackKind.Warning => "Warning: ",
            _ => string.Empty
        };

        writer.WriteLine(prefix + result.Message.Trim());
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            $"""
            {GetVersionText()}

            Usage:
              nanoai [options]                    Start the interactive terminal UI
              nanoai [options] "<prompt>"         Run one prompt and print the response
              nanoai [options] --prompt "<text>"  Run one prompt and print the response
              echo "<prompt>" | nanoai [options]  Run one prompt from standard input
              nanoai --acp [options]              Run an Agent Client Protocol server

            Options:
              --acp                Speak ACP over stdin/stdout for compatible editors
              --interactive        Start the terminal UI explicitly
              --stdin              Read the one-shot prompt from standard input
              --json               Write one-shot result as a JSON object
              -y, --yes            Approve promptable tool requests for this run
              -p, --prompt <text>  One-shot prompt text
              --sandbox-mode <mode>
                                   Override sandbox mode: read-only, workspace-write, or danger-full-access
              --provider-auth-key <key>
                                   Use this key for provider API-key onboarding
              --section <id>       Resume an existing section
              --session <id>       Alias for --section
              --no-update-check    Skip checking for application updates on startup
              --no-old-reader      Resume a section without replaying old messages to the screen
              --profile <name>     Use an agent profile
              --thinking <on|off>  Override thinking mode
              -v, --version        Show version
              -h, --help           Show help
              --doctor             Run system diagnostics and print doctor report

            Note:
              Run nanoai once to complete provider setup before using one-shot prompts.
            """);
    }

    private static string GetVersionText()
    {
        return $"NanoAgent CLI {ProductTelemetryHelpers.GetNanoAgentVersion()}";
    }

    private static void WriteFatalExitMessage(AppState state)
    {
        if (string.IsNullOrWhiteSpace(state.FatalExitMessage))
        {
            return;
        }

        Console.WriteLine(state.FatalExitMessage.Trim());
    }

    private static void WriteExitResumeHint(AppState state)
    {
        if (string.IsNullOrWhiteSpace(state.SessionId) ||
            string.IsNullOrWhiteSpace(state.SectionResumeCommand))
        {
            return;
        }

        Console.WriteLine("Exiting NanoAgent.");
        Console.WriteLine($"Section: {state.SessionId}");
        Console.WriteLine($"Resume this section: {state.SectionResumeCommand}");
    }

    private static int GetHeaderPanelSize(AppState state)
    {
        return state.HasMadeFirstLlmCall ? 3 : 9;
    }

    private static void StartInitialization(
        AppState state,
        bool noOldReader = false)
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
                    ApplySessionInfo(appState, sessionInfo);
                    if (!noOldReader)
                    {
                        RenderResumedSection(appState, sessionInfo);
                    }
                });
            }
            catch (OperationCanceledException) when (state.LifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (SectionWorkspaceMismatchException exception)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.HasFatalError = true;
                    appState.ActivityText = "Backend startup failed";
                    appState.FatalExitMessage = exception.Message;
                    appState.AddSystemMessage(exception.Message);
                    appState.Running = false;
                });
            }
            catch (Exception exception)
            {
                state.UiBridge.Enqueue(appState =>
                {
                    appState.IsBusy = false;
                    appState.HasFatalError = true;
                    appState.ActivityText = "Backend startup failed";
                    appState.FatalExitMessage = $"Failed to start NanoAgent: {exception.Message}";
                    appState.AddSystemMessage(appState.FatalExitMessage);
                    appState.Running = false;
                });
            }
        });
    }

    private static void ApplySessionInfo(
        AppState state,
        BackendSessionInfo sessionInfo)
    {
        state.SessionId = sessionInfo.SessionId;
        state.SectionResumeCommand = sessionInfo.SectionResumeCommand;
        state.ProviderName = sessionInfo.ProviderName;
        state.ActiveModelId = sessionInfo.ModelId;
        state.ActiveModelContextWindowTokens = sessionInfo.ActiveModelContextWindowTokens;
        state.ReasoningEffort = sessionInfo.ReasoningEffort;
    }

    private static void RenderResumedSection(
        AppState state,
        BackendSessionInfo sessionInfo)
    {
        if (!sessionInfo.IsResumedSection)
        {
            return;
        }

        string sectionTitle = string.IsNullOrWhiteSpace(sessionInfo.SectionTitle)
            ? "Untitled section"
            : sessionInfo.SectionTitle.Trim();

        RenderSessionView(
            state,
            sessionInfo,
            $"Resumed section: {sectionTitle}\n" +
            $"Section: {sessionInfo.SessionId}\n" +
            $"Resume command: {sessionInfo.SectionResumeCommand}");
    }

    private static void RenderSessionView(
        AppState state,
        BackendSessionInfo sessionInfo,
        string? statusMessage)
    {
        state.ClearPlanState();
        state.Messages.Clear();
        state.ResetConversationViewport();
        state.HasMadeFirstLlmCall = false;

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            state.AddSystemMessage(statusMessage.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sessionInfo.SessionContentText))
        {
            state.AddSystemMessage(
                "Restored session content:\n\n" +
                sessionInfo.SessionContentText.Trim());
        }

        foreach (BackendConversationMessage message in sessionInfo.ConversationHistory)
        {
            Role? role = message.Role switch
            {
                "user" => Role.User,
                "assistant" => Role.Assistant,
                "tool" => Role.System,
                _ => null
            };

            if (role is not null && !string.IsNullOrWhiteSpace(message.Content))
            {
                if (sessionInfo.ShowThinking &&
                    role == Role.Assistant &&
                    !string.IsNullOrWhiteSpace(message.ReasoningContent))
                {
                    state.AddThinkingMessage("Thinking:\n\n" + message.ReasoningContent.Trim());
                }

                state.AddMessage(role.Value, message.Content);
            }
        }
    }

    private static string[] EnsureStartupPromptsArg(
        IReadOnlyList<string> args,
        bool enabled)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, "--startup-prompts", StringComparison.OrdinalIgnoreCase))
            {
                return [.. args];
            }

            if (arg.StartsWith("--startup-prompts=", StringComparison.OrdinalIgnoreCase))
            {
                return [.. args];
            }
        }

        return [.. args, "--startup-prompts", enabled ? "enabled" : "disabled"];
    }
}
