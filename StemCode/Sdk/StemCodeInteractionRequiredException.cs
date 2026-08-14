namespace StemCode.Sdk;

/// <summary>
/// Thrown when the agent needs an interactive answer (for example a provider
/// onboarding selection or an API key) but the embedding application has not
/// supplied the information up front and no <see cref="IAgentInteractionHandler"/>
/// is configured. Configure the provider and API key on
/// <see cref="StemCodeClientBuilder"/> so onboarding is skipped, or register a
/// handler via <see cref="StemCodeClientBuilder.UseInteractionHandler"/>.
/// </summary>
public sealed class StemCodeInteractionRequiredException : InvalidOperationException
{
    public StemCodeInteractionRequiredException(string message)
        : base(message)
    {
    }

    public StemCodeInteractionRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
