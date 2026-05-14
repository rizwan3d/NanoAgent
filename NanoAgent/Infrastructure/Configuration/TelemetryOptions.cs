namespace NanoAgent.Infrastructure.Configuration;

public sealed class TelemetryOptions
{
    public const string DefaultHost = "https://us.i.posthog.com";

    public const string DefaultProjectToken = "phc_AKZFSyU239kkQ5GQ2y4idb8MtFX96kVekgezgnsELHRk";

    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = DefaultHost;

    public string? ProjectToken { get; set; } = DefaultProjectToken;
}
