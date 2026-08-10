namespace StemCode.Application.Models;

public sealed record ApplicationUpdateInstallResult(
    bool IsSuccess,
    string Message);
