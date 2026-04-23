namespace NanoAgent.Presentation.Abstractions;

public interface IReplInputReader
{
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
}
