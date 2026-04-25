using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Exceptions;

public sealed class ConversationPipelineException : Exception
{
    public ConversationPipelineException(string message)
        : base(SecretRedactor.Redact(message))
    {
    }

    public ConversationPipelineException(string message, Exception innerException)
        : base(SecretRedactor.Redact(message), innerException)
    {
    }
}
