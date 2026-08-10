using StemCode.Application.Utilities;

namespace StemCode.Application.Exceptions;

public sealed class ConversationProviderException : Exception
{
    public ConversationProviderException(string message)
        : base(SecretRedactor.Redact(message))
    {
    }

    public ConversationProviderException(string message, Exception innerException)
        : base(SecretRedactor.Redact(message), innerException)
    {
    }
}
