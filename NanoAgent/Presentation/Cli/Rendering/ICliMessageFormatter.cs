namespace NanoAgent.Presentation.Cli.Rendering;

internal interface ICliMessageFormatter
{
    CliRenderDocument Format(
        CliRenderMessageKind kind,
        string message);
}
