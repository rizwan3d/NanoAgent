namespace FinalAgent.Application.Abstractions;

public interface IApplicationRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}
