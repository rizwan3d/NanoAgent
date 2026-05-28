using Microsoft.Extensions.Logging;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Exceptions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Anthropic;
using NanoAgent.Infrastructure.GitHub;
using NanoAgent.Infrastructure.OpenAi;

namespace NanoAgent.Infrastructure.Conversation;

internal sealed class OpenAiCompatibleConversationProviderClient : IConversationProviderClient
{
    private readonly IReadOnlyList<IConversationProviderAdapter> _adapters;

    public OpenAiCompatibleConversationProviderClient(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleConversationProviderClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<double>? nextJitter = null,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService = null,
        IAnthropicClaudeAccountCredentialService? anthropicClaudeAccountCredentialService = null,
        IGitHubCopilotCredentialService? gitHubCopilotCredentialService = null)
        : this(CreateAdapters(
            httpClient,
            logger,
            delayAsync,
            nextJitter,
            openAiChatGptAccountCredentialService,
            anthropicClaudeAccountCredentialService,
            gitHubCopilotCredentialService))
    {
    }

    internal OpenAiCompatibleConversationProviderClient(
        IReadOnlyList<IConversationProviderAdapter> adapters)
    {
        _adapters = adapters;
    }

    public Task<ConversationProviderPayload> SendAsync(
        ConversationProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IConversationProviderAdapter? adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(request));
        if (adapter is null)
        {
            throw new ConversationProviderException(
                $"No conversation provider adapter is registered for provider kind '{request.ProviderProfile.ProviderKind}'.");
        }

        return adapter.SendAsync(request, cancellationToken);
    }

    private static IReadOnlyList<IConversationProviderAdapter> CreateAdapters(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleConversationProviderClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync,
        Func<double>? nextJitter,
        IOpenAiChatGptAccountCredentialService? openAiChatGptAccountCredentialService,
        IAnthropicClaudeAccountCredentialService? anthropicClaudeAccountCredentialService,
        IGitHubCopilotCredentialService? gitHubCopilotCredentialService)
    {
        ConversationProviderRequestPayloadFactory payloadFactory = new();
        ConversationProviderResponseNormalizer responseNormalizer = new();
        ConversationProviderHttpExecutor httpExecutor = new(httpClient, logger, delayAsync, nextJitter);

        return
        [
            new OpenAiChatGptAccountConversationProviderAdapter(
                httpExecutor,
                payloadFactory,
                responseNormalizer,
                openAiChatGptAccountCredentialService),
            new AnthropicClaudeAccountConversationProviderAdapter(
                httpExecutor,
                payloadFactory,
                responseNormalizer,
                anthropicClaudeAccountCredentialService),
            new GitHubCopilotConversationProviderAdapter(
                httpExecutor,
                payloadFactory,
                gitHubCopilotCredentialService),
            new OllamaCloudConversationProviderAdapter(
                httpExecutor,
                payloadFactory,
                responseNormalizer),
            new OpenCodeZenConversationProviderAdapter(
                httpExecutor,
                payloadFactory,
                responseNormalizer),
            new OpenAiCompatibleConversationProviderAdapter(
                httpExecutor,
                payloadFactory)
        ];
    }
}
