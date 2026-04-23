namespace NanoAgent.Presentation.Abstractions;

public interface IApplicationRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}
