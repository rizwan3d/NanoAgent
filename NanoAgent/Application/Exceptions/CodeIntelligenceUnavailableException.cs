namespace NanoAgent.Application.Exceptions;

public sealed class CodeIntelligenceUnavailableException : Exception
{
    public CodeIntelligenceUnavailableException(
        string message,
        IReadOnlyList<string>? attempts = null)
        : base(message)
    {
        Attempts = attempts ?? [];
    }

    public IReadOnlyList<string> Attempts { get; }
}
