using StemCode.Application.Utilities;

namespace StemCode.Application.Exceptions;

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
