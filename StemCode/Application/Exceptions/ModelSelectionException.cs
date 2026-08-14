namespace StemCode.Application.Exceptions;

public sealed class ModelSelectionException : ModelDiscoveryException
{
    public ModelSelectionException(string message)
        : base(message)
    {
    }
}
