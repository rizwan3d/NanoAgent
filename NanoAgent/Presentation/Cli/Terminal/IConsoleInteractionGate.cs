namespace NanoAgent.Presentation.Cli.Terminal;

internal interface IConsoleInteractionGate
{
    IDisposable EnterScope();
}
