using System.Text;
using NanoAgent.Application.Backend;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using Spectre.Console;

namespace NanoAgent.CLI;

public static partial class Program
{
    private const string DefaultCompletionNote = "(0s \u00b7 0 tokens)";
    private const double EstimatedLiveTokensPerSecond = 4d;
    private const int HeaderDividerWidth = 53;
    private const int HeaderPanelSize = 10;
    private const int InputCursorBlinkIntervalMilliseconds = 500;
    private const int InputCursorColumnWidth = 1;
    private const int MessageScrollbarColumnWidth = 2;
    private const int MouseWheelScrollLineCount = 3;
    private const int MultilinePastePreviewLineThreshold = 3;
    private const int PasteContinuationReadTimeoutMilliseconds = 40;
    private const int TerminalSequenceReadTimeoutMilliseconds = 25;
    private const string RepositoryUrl = "github.com/rizwan3d/NanoAgent";
    private const string EnableAlternateScreenSequence = "\u001b[?1049h";
    private const string DisableAlternateScreenSequence = "\u001b[?1049l";
    private const string EnableBracketedPasteSequence = "\u001b[?2004h";
    private const string DisableBracketedPasteSequence = "\u001b[?2004l";
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

    public static async Task<int> Main(string[]? args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        CliInvocation invocation;
        try
        {
            invocation = CliInvocation.Parse(
                args ?? [],
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

        if (invocation.Mode == CliMode.SingleTurn)
        {
            return await RunSingleTurnAsync(
                invocation.BackendArgs,
                invocation.Prompt ?? string.Empty);
        }

        await RunInteractiveAsync(invocation.BackendArgs);
        return 0;
    }

    private static async Task RunInteractiveAsync(string[] args)
    {
        Console.CursorVisible = false;
        EnableTerminalWheelScrolling();

        UiBridge uiBridge = new();
        INanoAgentBackend backend = new NanoAgentBackend(args ?? []);
        AppState state = new(uiBridge, backend);
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            state.Running = false;
        };

        StartInitialization(state);
        Console.CancelKeyPress += cancelKeyPressHandler;

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
            Console.CancelKeyPress -= cancelKeyPressHandler;
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
                WriteFatalExitMessage(state);
                WriteExitResumeHint(state);
            }
        }
    }

    private static async Task<int> RunSingleTurnAsync(
        string[] args,
        string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.Error.WriteLine("No prompt was provided.");
            return 2;
        }

        ConsoleBridge uiBridge = new();
        string[] backendArgs = [..args, "--no-update-check"];
        await using INanoAgentBackend backend = new NanoAgentBackend(backendArgs);
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;

        try
        {
            await backend.InitializeAsync(uiBridge, cancellation.Token);

            string normalizedPrompt = prompt.Trim();
            if (normalizedPrompt.StartsWith("/", StringComparison.Ordinal))
            {
                BackendCommandResult commandResult = await backend.RunCommandAsync(
                    normalizedPrompt,
                    cancellation.Token);

                WriteCommandResult(commandResult.CommandResult);
                return commandResult.CommandResult.FeedbackKind == ReplFeedbackKind.Error ? 1 : 0;
            }

            ConversationTurnResult result = await backend.RunTurnAsync(
                normalizedPrompt,
                uiBridge,
                cancellation.Token);

            Console.WriteLine(result.ResponseText);
            return 0;
        }
        catch (PromptCancelledException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"NanoAgent error: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
        }
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
            """
            NanoAgent CLI

            Usage:
              nanoai [options]                    Start the interactive terminal UI
              nanoai [options] "<prompt>"         Run one prompt and print the response
              nanoai [options] --prompt "<text>"  Run one prompt and print the response
              echo "<prompt>" | nanoai [options]  Run one prompt from standard input

            Options:
              --interactive        Start the terminal UI explicitly
              --stdin              Read the one-shot prompt from standard input
              -p, --prompt <text>  One-shot prompt text
              --section <id>       Resume an existing section
              --session <id>       Alias for --section
              --profile <name>     Use an agent profile
              --thinking <effort>  Override thinking effort
              -h, --help           Show help

            Note:
              Run nanoai once to complete provider setup before using one-shot prompts.
            """);
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
                    ApplySessionInfo(appState, sessionInfo);
                    RenderResumedSection(appState, sessionInfo);
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
                    appState.AddSystemMessage($"Failed to start NanoAgent: {exception.Message}");
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
    }

    private static void RenderResumedSection(
        AppState state,
        BackendSessionInfo sessionInfo)
    {
        if (!sessionInfo.IsResumedSection)
        {
            return;
        }

        state.Messages.Clear();
        state.ConversationScrollOffset = 0;

        string sectionTitle = string.IsNullOrWhiteSpace(sessionInfo.SectionTitle)
            ? "Untitled section"
            : sessionInfo.SectionTitle.Trim();

        state.AddSystemMessage(
            $"Resumed section: {sectionTitle}\n" +
            $"Section: {sessionInfo.SessionId}\n" +
            $"Resume command: {sessionInfo.SectionResumeCommand}");

        foreach (BackendConversationMessage message in sessionInfo.ConversationHistory)
        {
            Role? role = message.Role switch
            {
                "user" => Role.User,
                "assistant" => Role.Assistant,
                _ => null
            };

            if (role is not null && !string.IsNullOrWhiteSpace(message.Content))
            {
                state.AddMessage(role.Value, message.Content);
            }
        }
    }
}
