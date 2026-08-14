using StemCode.Application.Backend;
using StemCode.Application.Exceptions;
using StemCode.Application.Models;
using StemCode.Infrastructure.Telemetry;
using Spectre.Console;
using System.Text;

namespace StemCode.CLI;

public static partial class Program
{
    private const double EstimatedLiveTokensPerSecond = 4d;
    private const int InputCursorBlinkIntervalMilliseconds = 500;
    private const int InputCursorColumnWidth = 1;
    private const int MessageScrollbarColumnWidth = 0;
    private const int MouseWheelScrollLineCount = 3;
    private const int MultilinePastePreviewLineThreshold = 3;
    private const int PasteContinuationReadTimeoutMilliseconds = 40;
    private const int ClipboardReadTimeoutMilliseconds = 2000;
    private const int MaxSlashCommandSuggestionCount = 8;
    private const int TerminalSequenceReadTimeoutMilliseconds = 25;
    private static readonly string[] Spinner =
    [
        "●           ",
        "● ●         ",
        "● ● ●       ",
        "● ● ● ●     ",
        "● ● ● ● ●   ",
        "● ● ● ● ● ● ",
    ];

    public static Task<int> Main(string[]? args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return CliApplication.RunAsync(args ?? []);
    }

    internal static bool TryHandleWindowsSandboxSpecialInvocation(
        IReadOnlyList<string> args,
        out int exitCode)
    {
        return SandboxInvocationParser.TryHandleSpecialInvocation(args, out exitCode);
    }

    internal static void WriteFatalExitMessage(AppState state)
    {
        if (string.IsNullOrWhiteSpace(state.FatalExitMessage))
        {
            return;
        }

        Console.WriteLine(state.FatalExitMessage.Trim());
    }

    internal static void WriteExitResumeHint(AppState state)
    {
        AnsiConsole.Write(new Markup(CliBranding.BuildHeaderBodyMarkup()));
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(state.SessionId) ||
            !string.IsNullOrWhiteSpace(state.SectionResumeCommand))
        {
            Console.WriteLine("Session information:");

            if (!string.IsNullOrWhiteSpace(state.SessionId))
            {
                Console.WriteLine($"  Section: {state.SessionId}");
            }

            if (!string.IsNullOrWhiteSpace(state.SectionResumeCommand))
            {
                Console.WriteLine($"  Resume:  {state.SectionResumeCommand}");
            }

            Console.WriteLine();
        }
    }

    private static int GetHeaderPanelSize(AppState state)
    {
        return state.HasMadeFirstLlmCall ? 3 : 9;
    }

    internal static void StartInitialization(
        AppState state,
        bool noOldReader = false)
    {
        state.IsBusy = true;
        state.ActivityText = "Loading StemCode services";

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
                    appState.FatalExitMessage = $"Failed to start StemCode: {exception.Message}";
                    appState.AddSystemMessage(appState.FatalExitMessage);
                    appState.Running = false;
                });
            }
        });
    }

    internal static void ApplySessionInfo(
       AppState state,
       BackendSessionInfo sessionInfo)
   {
       state.SessionId = sessionInfo.SessionId;
       state.SectionResumeCommand = sessionInfo.SectionResumeCommand;
       state.AgentProfileName = sessionInfo.AgentProfileName;
       state.ProviderName = sessionInfo.ProviderName;
       state.ActiveModelId = sessionInfo.ModelId;
       state.ActiveModelContextWindowTokens = sessionInfo.ActiveModelContextWindowTokens;
       state.ReasoningEffort = sessionInfo.ReasoningEffort;
        state.ThinkingMode = sessionInfo.ThinkingMode;
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

    internal static string[] EnsureStartupPromptsArg(
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
