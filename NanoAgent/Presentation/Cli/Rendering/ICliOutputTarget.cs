namespace NanoAgent.Presentation.Cli.Rendering;

internal interface ICliOutputTarget
{
    bool SupportsColor { get; }

    void WriteLine();

    void WriteLine(IReadOnlyList<CliOutputSegment> segments);
}
