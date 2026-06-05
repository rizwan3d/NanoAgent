namespace NanoAgent.Application.Abstractions;

public enum WindowsSandboxSetupState
{
    Ready,
    AlreadyReady,
    NotSupported,
    Skipped,
    Canceled,
    Failed
}

public sealed record WindowsSandboxSetupResult(
    WindowsSandboxSetupState State,
    string Message)
{
    public bool IsReady =>
        State is WindowsSandboxSetupState.Ready or
        WindowsSandboxSetupState.AlreadyReady or
        WindowsSandboxSetupState.NotSupported;
}
