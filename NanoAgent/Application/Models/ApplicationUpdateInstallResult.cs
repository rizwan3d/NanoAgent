namespace NanoAgent.Application.Models;

public sealed record ApplicationUpdateInstallResult(
    bool IsSuccess,
    string Message);
