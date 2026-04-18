using FinalAgent.Domain.Abstractions;
using FinalAgent.Domain.Models;

namespace FinalAgent.Domain.Services;

internal sealed class GreetingComposer : IGreetingComposer
{
    public string Compose(GreetingContext context)
    {
        return $"[{context.OccurredAt:O}] Hello {context.TargetName}. This is {context.OperatorName}.";
    }
}
