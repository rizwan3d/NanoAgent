namespace FinalAgent.Domain.Abstractions;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
