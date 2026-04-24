namespace NanoAgent.Infrastructure.Mcp;

internal sealed class McpProtocolException : Exception
{
    public McpProtocolException(string message)
        : base(message)
    {
    }

    public McpProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
