using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Logging;
using NanoAgent.Application.Models;
using Microsoft.Extensions.Logging;

namespace NanoAgent.Application.Repl.Services;

internal sealed class ReplRuntime : IReplRuntime
{
    private const string Utf8BomMojibakePrefix = "\u00EF\u00BB\u00BF";

    private readonly IReplInputReader _inputReader;
    private readonly IReplOutputWriter _outputWriter;
    private readonly IReplInterruptMonitor _interruptMonitor;
    private readonly IReplCommandParser _commandParser;
    private readonly IReplCommandDispatcher _commandDispatcher;
    private readonly IConversationPipeline _conversationPipeline;
    private readonly IReplSectionService _replSectionService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<ReplRuntime> _logger;

    public ReplRuntime(
        IReplInputReader inputReader,
        IReplOutputWriter outputWriter,
        IReplInterruptMonitor interruptMonitor,
        IReplCommandParser commandParser,
        IReplCommandDispatcher commandDispatcher,
        IConversationPipeline conversationPipeline,
        IReplSectionService replSectionService,
        ITokenEstimator tokenEstimator,
        ILogger<ReplRuntime> logger)
    {
        _inputReader = inputReader;
        _outputWriter = outputWriter;
        _interruptMonitor = interruptMonitor;
        _commandParser = commandParser;
        _commandDispatcher = commandDispatcher;
        _conversationPipeline = conversationPipeline;
        _replSectionService = replSectionService;
        _tokenEstimator = tokenEstimator;
        _logger = logger;
    }

    public async Task RunAsync(ReplSessionContext session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        ApplicationLogMessages.ReplStarted(_logger, session.ActiveModelId);

        await _outputWriter.WriteShellHeaderAsync(
            session.ApplicationName,
            session.ActiveModelId,
            cancellationToken);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? rawInput = await _inputReader.ReadLineAsync(cancellationToken);
                if (rawInput is null)
                {
                    ApplicationLogMessages.ReplInputClosed(_logger);
                    break;
                }

                string input = NormalizeInput(rawInput);
                if (input.Length == 0)
                {
                    continue;
                }

                if (input.StartsWith("/", StringComparison.Ordinal))
                {
                    ReplCommandResult commandResult;

                    try
                    {
                        ParsedReplCommand parsedCommand = _commandParser.Parse(input);

                        commandResult = await _commandDispatcher.DispatchAsync(
                            parsedCommand,
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
                    await _replSectionService.SaveIfDirtyAsync(session, cancellationToken);

                    if (commandResult.ExitRequested)
                    {
                        break;
                    }

                    continue;
                }

                _replSectionService.EnsureTitleGenerationStarted(session, input);
                CancellationTokenSource? requestCancellationSource = null;

                try
                {
                    int estimatedInputTokens = _tokenEstimator.Estimate(input);
                    ConversationTurnResult response;
                    requestCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    using (requestCancellationSource)
                    {
                        await using IAsyncDisposable interruptScope = await _interruptMonitor.StartMonitoringAsync(
                            requestCancellationSource,
                            cancellationToken);
                        await using (IResponseProgress progress = await _outputWriter.BeginResponseProgressAsync(
                                         estimatedInputTokens,
                                         session.TotalEstimatedOutputTokens,
                                         cancellationToken))
                        {
                            response = await _conversationPipeline.ProcessAsync(
                                input,
                                session,
                                progress,
                                requestCancellationSource.Token);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(response.ResponseText))
                    {
                        ConversationTurnMetrics? metrics = response.Metrics;
                        if (metrics is not null)
                        {
                            int sessionTotal = session.AddEstimatedOutputTokens(metrics.EstimatedOutputTokens);
                            metrics = metrics.WithSessionEstimatedOutputTokens(sessionTotal);
                        }

                        await _outputWriter.WriteResponseAsync(
                            response.ResponseText,
                            metrics,
                            cancellationToken);
                    }

                    await _replSectionService.SaveIfDirtyAsync(session, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (requestCancellationSource is not null &&
                                                        requestCancellationSource.IsCancellationRequested &&
                                                        !cancellationToken.IsCancellationRequested)
                {
                    ApplicationLogMessages.ReplConversationInterrupted(_logger);

                    await _outputWriter.WriteWarningAsync(
                        "Interrupted the current request. You can ask again or continue with a new prompt.",
                        cancellationToken);
                }
                catch (Exception exception) when (exception is ConversationPipelineException or
                                                  ConversationProviderException or
                                                  ConversationResponseException)
                {
                    ApplicationLogMessages.ReplConversationFailed(_logger, exception);

                    await _outputWriter.WriteErrorAsync(
                        $"{exception.Message} You can try again or use /exit to leave the shell.",
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    ApplicationLogMessages.ReplConversationFailed(_logger, exception);

                    await _outputWriter.WriteErrorAsync(
                        "The conversation pipeline failed unexpectedly. You can try again or use /exit to leave the shell.",
                        cancellationToken);
                }
            }
        }
        finally
        {
            await StopSectionAsync(session);
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
            : commandResult.FeedbackKind == ReplFeedbackKind.Warning
                ? _outputWriter.WriteWarningAsync(commandResult.Message, cancellationToken)
                : _outputWriter.WriteInfoAsync(commandResult.Message, cancellationToken);
    }

    private static string NormalizeInput(string rawInput)
    {
        ArgumentNullException.ThrowIfNull(rawInput);

        string normalizedInput = rawInput.Trim();

        if (normalizedInput.StartsWith(Utf8BomMojibakePrefix, StringComparison.Ordinal))
        {
            normalizedInput = normalizedInput[Utf8BomMojibakePrefix.Length..];
        }

        return normalizedInput.TrimStart('\uFEFF');
    }

    private async Task StopSectionAsync(ReplSessionContext session)
    {
        try
        {
            await _replSectionService.StopAsync(session, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Failed to flush section state for section {SectionId} during REPL shutdown.",
                session.SectionId);
        }

        try
        {
            await _outputWriter.WriteInfoAsync(
                $"Continue this section with: {session.SectionResumeCommand}",
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Failed to write resume command for section {SectionId}.",
                session.SectionId);
        }
    }
}
