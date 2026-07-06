namespace NanoAgent.CLI;

internal sealed class AcpProtocolException : Exception
{
    public AcpProtocolException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}

internal sealed class AcpRemoteException : Exception
{
    public AcpRemoteException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}

internal sealed class IncomingLineTooLongException : Exception
{
    public IncomingLineTooLongException(int maxLineLength)
        : base($"Incoming ACP line exceeds the maximum size of {maxLineLength} characters.")
    {
    }
}
