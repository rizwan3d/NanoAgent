using Microsoft.Extensions.Options;

namespace FinalAgent.Infrastructure.Configuration;

public sealed class ApplicationOptionsValidator : IValidateOptions<ApplicationOptions>
{
    public ValidateOptionsResult Validate(string? name, ApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.OperatorName))
        {
            failures.Add($"{ApplicationOptions.SectionName}:OperatorName must be provided.");
        }

        if (string.IsNullOrWhiteSpace(options.TargetName))
        {
            failures.Add($"{ApplicationOptions.SectionName}:TargetName must be provided.");
        }

        if (options.RepeatCount is < 1 or > 100)
        {
            failures.Add($"{ApplicationOptions.SectionName}:RepeatCount must be between 1 and 100.");
        }

        if (options.DelayMilliseconds is < 0 or > 60000)
        {
            failures.Add($"{ApplicationOptions.SectionName}:DelayMilliseconds must be between 0 and 60000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
