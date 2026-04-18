using FinalAgent.Domain.Abstractions;

namespace FinalAgent.Infrastructure.Time;

internal sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
