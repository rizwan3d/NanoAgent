using NanoAgent.Application.Models;

namespace NanoAgent.Sdk;

/// <summary>
/// Optional hook that lets an embedding application answer interactive prompts
/// (provider selections, free-text values, and secrets) programmatically instead
/// of through a console. Register it with
/// <see cref="NanoAgentClientBuilder.UseInteractionHandler"/>.
///
/// When a provider and API key are supplied on the builder, onboarding is skipped
/// and these methods are normally never called; a handler is only needed when you
/// intentionally leave configuration to be resolved at runtime.
/// </summary>
public interface IAgentInteractionHandler
{
    /// <summary>Answers a selection prompt by returning one of the request options' values.</summary>
    Task<T> ProvideSelectionAsync<T>(
        SelectionPromptRequest<T> request,
        CancellationToken cancellationToken);

    /// <summary>Answers a free-text or secret prompt. <paramref name="isSecret"/> indicates a secret (for example an API key).</summary>
    Task<string> ProvideTextAsync(
        TextPromptRequest request,
        bool isSecret,
        CancellationToken cancellationToken);
}
