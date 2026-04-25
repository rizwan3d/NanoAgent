using Microsoft.Extensions.Options;
using NanoAgent.Application.Models;

namespace NanoAgent.Infrastructure.Configuration;

public sealed class ApplicationOptionsValidator : IValidateOptions<ApplicationOptions>
{
    public ValidateOptionsResult Validate(string? name, ApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.Conversation is null)
        {
            failures.Add($"{ApplicationOptions.SectionName}:Conversation must be provided.");
        }
        else if (options.Conversation.RequestTimeoutSeconds < 0)
        {
            failures.Add($"{ApplicationOptions.SectionName}:Conversation:RequestTimeoutSeconds must be zero or greater.");
        }
        else if (options.Conversation.MaxHistoryTurns < 0)
        {
            failures.Add($"{ApplicationOptions.SectionName}:Conversation:MaxHistoryTurns must be zero or greater.");
        }
        else if (options.Conversation.MaxToolRoundsPerTurn < 0)
        {
            failures.Add($"{ApplicationOptions.SectionName}:Conversation:MaxToolRoundsPerTurn must be zero or greater. Use zero for no tool-round limit.");
        }

        if (options.ModelSelection is null)
        {
            failures.Add($"{ApplicationOptions.SectionName}:ModelSelection must be provided.");
        }
        else
        {
            if (options.ModelSelection.CacheDurationSeconds <= 0)
            {
                failures.Add($"{ApplicationOptions.SectionName}:ModelSelection:CacheDurationSeconds must be greater than zero.");
            }
        }

        if (options.Permissions is null)
        {
            failures.Add($"{ApplicationOptions.SectionName}:Permissions must be provided.");
        }

        if (options.Hooks is null)
        {
            failures.Add($"{ApplicationOptions.SectionName}:Hooks must be provided.");
        }
        else
        {
            if (options.Hooks.DefaultTimeoutSeconds <= 0)
            {
                failures.Add($"{ApplicationOptions.SectionName}:Hooks:DefaultTimeoutSeconds must be greater than zero.");
            }

            if (options.Hooks.MaxOutputCharacters < 0)
            {
                failures.Add($"{ApplicationOptions.SectionName}:Hooks:MaxOutputCharacters must be zero or greater.");
            }

            foreach ((LifecycleHookRule rule, int index) in (options.Hooks.Rules ?? []).Select((rule, index) => (rule, index)))
            {
                if (rule is null)
                {
                    failures.Add($"{ApplicationOptions.SectionName}:Hooks:Rules:{index} must be provided.");
                    continue;
                }

                if (rule.Enabled && string.IsNullOrWhiteSpace(rule.Command))
                {
                    failures.Add($"{ApplicationOptions.SectionName}:Hooks:Rules:{index}:Command must be provided for enabled hook rules.");
                }

                if (rule.TimeoutSeconds is <= 0)
                {
                    failures.Add($"{ApplicationOptions.SectionName}:Hooks:Rules:{index}:TimeoutSeconds must be greater than zero when provided.");
                }

                if (rule.MaxOutputCharacters is < 0)
                {
                    failures.Add($"{ApplicationOptions.SectionName}:Hooks:Rules:{index}:MaxOutputCharacters must be zero or greater when provided.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
