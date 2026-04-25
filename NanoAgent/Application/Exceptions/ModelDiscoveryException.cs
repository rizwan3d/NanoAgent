using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Exceptions;

public class ModelDiscoveryException : InvalidOperationException
{
    public ModelDiscoveryException(string message)
        : base(SecretRedactor.Redact(message))
    {
    }

    public ModelDiscoveryException(string message, Exception innerException)
        : base(SecretRedactor.Redact(message), innerException)
    {
    }
}
