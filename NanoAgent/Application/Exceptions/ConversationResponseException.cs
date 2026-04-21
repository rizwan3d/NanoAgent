namespace NanoAgent.Application.Exceptions;

public sealed class ConversationResponseException : Exception
{
    public ConversationResponseException(
        string message,
        bool isRetryableEmptyResponse = false)
        : base(message)
    {
        IsRetryableEmptyResponse = isRetryableEmptyResponse;
    }

    public ConversationResponseException(
        string message,
        Exception innerException,
        bool isRetryableEmptyResponse = false)
        : base(message, innerException)
    {
        IsRetryableEmptyResponse = isRetryableEmptyResponse;
    }

    public bool IsRetryableEmptyResponse { get; }
}
