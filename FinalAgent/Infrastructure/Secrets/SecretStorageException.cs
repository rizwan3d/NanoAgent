namespace FinalAgent.Infrastructure.Secrets;

internal sealed class SecretStorageException : InvalidOperationException
{
    public SecretStorageException(string message)
        : base(message)
    {
    }

    public SecretStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static SecretStorageException ForOperation(
        string storeName,
        string operation,
        string? detail)
    {
        string message = string.IsNullOrWhiteSpace(detail)
            ? $"Unable to {operation} the secret in {storeName}."
            : $"Unable to {operation} the secret in {storeName}. {detail.Trim()}";

        return new SecretStorageException(message);
    }
}
