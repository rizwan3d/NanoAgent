using FinalAgent.Application.Models;

namespace FinalAgent.Application.Abstractions;

public interface IReplRuntime
{
    Task RunAsync(ReplSessionContext session, CancellationToken cancellationToken);
}
