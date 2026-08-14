namespace StemCode.Application.Exceptions;

public sealed class PromptCancelledException : OperationCanceledException
{
    public PromptCancelledException()
        : base("The interactive prompt was cancelled.")
    {
    }

    public PromptCancelledException(string message)
        : base(message)
    {
    }
}
