namespace NanoAgent.ConsoleHost.Rendering;

internal sealed class ConsoleRenderSettings
{
    public bool EnableAnimations { get; init; } = true;

    public TimeSpan AssistantBlockDelay { get; init; } = TimeSpan.FromMilliseconds(32);

    public TimeSpan HeaderLineDelay { get; init; } = TimeSpan.FromMilliseconds(18);
}
