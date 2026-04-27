using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Exceptions;

public sealed class ConversationResponseException : Exception
{
    public ConversationResponseException(
        string message,
        bool isRetryableEmptyResponse = false,
        bool isRetryableRawToolCallResponse = false,
        bool isRetryableIncompletePlanResponse = false,
        bool isRetryableOutputLimitResponse = false)
        : base(SecretRedactor.Redact(message))
    {
        IsRetryableEmptyResponse = isRetryableEmptyResponse;
        IsRetryableRawToolCallResponse = isRetryableRawToolCallResponse;
        IsRetryableIncompletePlanResponse = isRetryableIncompletePlanResponse;
        IsRetryableOutputLimitResponse = isRetryableOutputLimitResponse;
    }

    public ConversationResponseException(
        string message,
        Exception innerException,
        bool isRetryableEmptyResponse = false,
        bool isRetryableRawToolCallResponse = false,
        bool isRetryableIncompletePlanResponse = false,
        bool isRetryableOutputLimitResponse = false)
        : base(SecretRedactor.Redact(message), innerException)
    {
        IsRetryableEmptyResponse = isRetryableEmptyResponse;
        IsRetryableRawToolCallResponse = isRetryableRawToolCallResponse;
        IsRetryableIncompletePlanResponse = isRetryableIncompletePlanResponse;
        IsRetryableOutputLimitResponse = isRetryableOutputLimitResponse;
    }

    public bool IsRetryableEmptyResponse { get; }

    public bool IsRetryableRawToolCallResponse { get; }

    public bool IsRetryableIncompletePlanResponse { get; }

    public bool IsRetryableOutputLimitResponse { get; }

    public bool IsRetryableProviderOutput =>
        IsRetryableEmptyResponse ||
        IsRetryableRawToolCallResponse ||
        IsRetryableIncompletePlanResponse ||
        IsRetryableOutputLimitResponse;
}
