namespace NanoAgent.Presentation.Cli.Rendering;

internal interface ICliTextRenderer
{
    Task RenderAsync(
        CliRenderDocument document,
        CancellationToken cancellationToken);
}
