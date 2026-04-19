using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Logging;
using FinalAgent.Application.Models;
using Microsoft.Extensions.Logging;

namespace FinalAgent.Application.Repl.Services;

internal sealed class ReplRuntime : IReplRuntime
{
    private readonly IReplInputReader _inputReader;
    private readonly IReplOutputWriter _outputWriter;
    private readonly IReplCommandDispatcher _commandDispatcher;
    private readonly IConversationPipeline _conversationPipeline;
    private readonly ILogger<ReplRuntime> _logger;

    public ReplRuntime(
        IReplInputReader inputReader,
        IReplOutputWriter outputWriter,
        IReplCommandDispatcher commandDispatcher,
        IConversationPipeline conversationPipeline,
        ILogger<ReplRuntime> logger)
    {
        _inputReader = inputReader;
        _outputWriter = outputWriter;
        _commandDispatcher = commandDispatcher;
        _conversationPipeline = conversationPipeline;
        _logger = logger;
    }

    public async Task RunAsync(ReplSessionContext session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        ApplicationLogMessages.ReplStarted(_logger, session.SelectedModelId);

        await _outputWriter.WriteInfoAsync(
            $"Shell ready. Provider: {session.ProviderName}. Model: {session.SelectedModelId}. Type /help for commands.",
            cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? rawInput = await _inputReader.ReadLineAsync(cancellationToken);
            if (rawInput is null)
            {
                ApplicationLogMessages.ReplInputClosed(_logger);
                break;
            }

            string input = rawInput.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (input.StartsWith("/", StringComparison.Ordinal))
            {
                ReplCommandResult commandResult;

                try
                {
                    commandResult = await _commandDispatcher.DispatchAsync(
                        input,
                        session,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ApplicationLogMessages.ReplCommandFailed(_logger, input, exception);

                    await _outputWriter.WriteErrorAsync(
                        "The command failed unexpectedly. You can retry or use /exit to leave the shell.",
                        cancellationToken);

                    continue;
                }

                await WriteCommandFeedbackAsync(commandResult, cancellationToken);

                if (commandResult.ExitRequested)
                {
                    break;
                }

                continue;
            }

            try
            {
                ConversationTurnResult response = await _conversationPipeline.ProcessAsync(
                    input,
                    session,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(response.ResponseText))
                {
                    await _outputWriter.WriteResponseAsync(response.ResponseText, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ApplicationLogMessages.ReplConversationFailed(_logger, exception);

                await _outputWriter.WriteErrorAsync(
                    "The conversation pipeline failed unexpectedly. You can try again or use /exit to leave the shell.",
                    cancellationToken);
            }
        }

        ApplicationLogMessages.ReplStopped(_logger);
    }

    private Task WriteCommandFeedbackAsync(
        ReplCommandResult commandResult,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commandResult.Message))
        {
            return Task.CompletedTask;
        }

        return commandResult.FeedbackKind == ReplFeedbackKind.Error
            ? _outputWriter.WriteErrorAsync(commandResult.Message, cancellationToken)
            : _outputWriter.WriteInfoAsync(commandResult.Message, cancellationToken);
    }
}
