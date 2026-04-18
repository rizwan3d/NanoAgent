using FinalAgent.Domain.Models;

namespace FinalAgent.Domain.Abstractions;

public interface IGreetingComposer
{
    string Compose(GreetingContext context);
}
