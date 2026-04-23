using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Abstractions;

public interface IReplRuntime
{
    Task RunAsync(ReplSessionContext session, CancellationToken cancellationToken);
}
