namespace FinalAgent.Infrastructure.Configuration;

public sealed class ApplicationOptions
{
    public const string SectionName = "Application";

    public string OperatorName { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public int RepeatCount { get; set; } = 1;

    public int DelayMilliseconds { get; set; } = 0;
}
