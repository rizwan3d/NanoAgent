namespace StemCode.Application.Exceptions;

public sealed class ModelProviderException : ModelDiscoveryException
{
    public ModelProviderException(string message)
        : base(message)
    {
    }

    public ModelProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
