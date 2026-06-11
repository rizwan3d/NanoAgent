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
    private const int HeaderPanelSize = 9;
    private const int InputCursorBlinkIntervalMilliseconds = 500;
    private const int InputCursorColumnWidth = 1;
    private const int MessageScrollbarColumnWidth = 2;
    private const int MouseWheelScrollLineCount = 3;
    private const int MultilinePastePreviewLineThreshold = 3;
    private const int PasteContinuationReadTimeoutMilliseconds = 40;
    private const int MaxSlashCommandSuggestionCount = 8;
    private const int TerminalSequenceReadTimeoutMilliseconds = 25;
    private const string EnableAlternateScreenSequence = "\u001b[?1049h";
    private const string DisableAlternateScreenSequence = "\u001b[?1049l";
    private const string EnableBracketedPasteSequence = "\u001b[?2004h";
    private const string DisableBracketedPasteSequence = "\u001b[?2004l";
    private const string EnableWheelScrollingSequence = "\u001b[?1007h";
    private const string DisableWheelScrollingSequence = "\u001b[?1007l";
    private const string DisableMouseTrackingSequence = "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l";
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
    private static readonly SlashCommandSuggestion[] SlashCommandSuggestions =
    [
        new("/allow", "/allow <tool-or-tag> [pattern]", "Add a session-scoped allow override.", true),
        new("/budget", "/budget [status|local|cloud]", "Show or configure budget controls.", false),
        new("/clear", "/clear", "Clear the terminal conversation view.", false),
        new("/clone", "/clone", "Duplicate the current session.", false),
        new("/compact", "/compact [retained-turns]", "Manually compact session context.", false),
        new("/config", "/config", "Show provider, session, profile, thinking, and model details.", false),
        new("/copy", "/copy", "Copy the last agent message.", false),
        new("/doctor", "/doctor", "Show comprehensive system diagnostics.", false),
        new("/deny", "/deny <tool-or-tag> [pattern]", "Add a session-scoped deny override.", true),
        new("/exit", "/exit", "Exit the interactive shell.", false),
        new("/export", "/export [json|html] [path]", "Export session as JSON or HTML.", false),
        new("/fork", "/fork [turn-number]", "Fork from a previous user message.", false),
        new("/help", "/help", "List available commands and usage.", false),
        new("/import", "/import <json-path>", "Import a session from JSON.", true),
        new("/init", "/init [recommended|minimal|custom]", "Choose workspace-local NanoAgent files.", false),
        new("/ls", "/ls", "List files in the current workspace.", false),
        new("/lsp", "/lsp [status|refresh|file <path> [refresh]]", "Show detected language servers or inspect a file.", false),
        new("/mcp", "/mcp", "Show configured MCP servers and dynamic tools.", false),
        new("/models", "/models", "Choose the active model with the picker.", false),
        new("/new", "/new", "Start a new session.", false),
        new("/onboard", "/onboard", "Open provider onboarding menus.", false),
        new("/permissions", "/permissions", "Show permission policy and override guidance.", false),
        new("/provider", "/provider [list|name]", "List or switch saved providers.", false),
        new("/profile", "/profile <name>", "Switch the active agent profile.", true),
        new("/redact", "/redact [on|off]", "Show or toggle secret redaction.", false),
        new("/read", "/read <file>", "Read a workspace file after confirmation.", true),
        new("/reload", "/reload", "Reload local resources.", false),
        new("/redo", "/redo", "Re-apply the most recently undone file edit.", false),
        new("/resume", "/resume [session-id]", "Resume a saved session.", false),
        new("/rules", "/rules", "List effective permission rules.", false),
        new("/session", "/session", "Show session info and stats.", false),
        new("/setting", "/setting [area]", "Open the NanoAgent settings picker.", false),
        new("/share", "/share", "Share session as a secret GitHub gist.", false),
        new("/setup-sandbox", "/setup-sandbox", "Set up Windows sandbox support for restricted shell commands.", false),
        new("/terminals", "/terminals [stop <id>|stop all]", "List or stop background terminals.", false),
        new("/thinking", "/thinking [on|off]", "Show or set simple thinking mode.", false),
        new("/tree", "/tree", "Navigate saved sessions.", false),
        new("/undo", "/undo", "Roll back the most recent tracked file edit.", false),
        new("/update", "/update [now]", "Check for updates.", false),
        new("/use", "/use <model>", "Switch the active model directly.", true),
        new("/version", "/version", "Show the current NanoAgent CLI version.", false)
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
                    BackendRuntimeOptions.CliSurface,
                    skipUpdateCheck: true),
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
                invocation.AutoApproveAllTools);
        }

        await RunInteractiveAsync(
            invocation.RuntimeArguments.WithDefaults(BackendRuntimeOptions.CliSurface),
            invocation.ProviderAuthKey,
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
        bool autoApproveAllTools)
    {
        AcpServer server = new(
            Console.In,
            Console.Out,
            Console.Error,
            args,
            providerAuthKey,
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
        bool autoApproveAllTools)
    {
        Console.CursorVisible = false;
        EnableTerminalWheelScrolling();

        UiBridge uiBridge = new(providerAuthKey);
        BackendRuntimeArguments interactiveRuntimeArguments = BackendRuntimeArguments.Parse(
                EnsureStartupPromptsArg(runtimeArguments.RawArgs, enabled: true))
            .WithDefaults(
                runtimeArguments.EffectiveAppSurface(BackendRuntimeOptions.CliSurface),
                runtimeArguments.SkipUpdateCheck);
        INanoAgentBackend backend = new NanoAgentBackend(
            interactiveRuntimeArguments,
            sessionMcpServers: [],
            autoApproveAllTools);
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
              --provider-auth-key <key>
                                   Use this key for provider API-key onboarding
              --section <id>       Resume an existing section
              --session <id>       Alias for --section
              --profile <name>     Use an agent profile
              --thinking <effort>  Override thinking effort
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
        state.ActiveModelContextWindowTokens = sessionInfo.ActiveModelContextWindowTokens;
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
        state.ConversationScrollOffset = 0;

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            state.AddSystemMessage(statusMessage.Trim());
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
                if (role == Role.Assistant && !string.IsNullOrWhiteSpace(message.ReasoningContent))
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
