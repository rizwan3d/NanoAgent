namespace FinalAgent.Application.Abstractions;

public interface IReplOutputWriter
{
    Task WriteInfoAsync(string message, CancellationToken cancellationToken);

    Task WriteErrorAsync(string message, CancellationToken cancellationToken);

    Task WriteResponseAsync(string message, CancellationToken cancellationToken);
}
