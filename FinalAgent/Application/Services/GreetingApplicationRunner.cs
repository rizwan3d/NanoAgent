using FinalAgent.Application.Abstractions;
using FinalAgent.Application.Logging;
using FinalAgent.Domain.Abstractions;
using FinalAgent.Domain.Models;
using FinalAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinalAgent.Application.Services;

internal sealed class GreetingApplicationRunner : IApplicationRunner
{
    private readonly IGreetingComposer _greetingComposer;
    private readonly ISystemClock _systemClock;
    private readonly ApplicationOptions _options;
    private readonly ILogger<GreetingApplicationRunner> _logger;

    public GreetingApplicationRunner(
        IGreetingComposer greetingComposer,
        ISystemClock systemClock,
        IOptions<ApplicationOptions> options,
        ILogger<GreetingApplicationRunner> logger)
    {
        _greetingComposer = greetingComposer;
        _systemClock = systemClock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ApplicationLogMessages.RunnerStarted(_logger, _options.TargetName, _options.RepeatCount);

        TimeSpan delay = TimeSpan.FromMilliseconds(_options.DelayMilliseconds);

        for (int iteration = 1; iteration <= _options.RepeatCount; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GreetingContext context = new(
                _options.OperatorName,
                _options.TargetName,
                _systemClock.UtcNow);

            string message = _greetingComposer.Compose(context);
            ApplicationLogMessages.GreetingPublished(_logger, iteration, _options.RepeatCount, message);

            if (iteration < _options.RepeatCount && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        ApplicationLogMessages.RunnerCompleted(_logger);
    }
}
