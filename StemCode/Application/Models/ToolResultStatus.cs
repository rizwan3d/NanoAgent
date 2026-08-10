namespace StemCode.Application.Models;

public enum ToolResultStatus
{
    Success = 1,
    InvalidArguments = 2,
    NotFound = 3,
    ExecutionError = 4,
    PermissionDenied = 5
}
