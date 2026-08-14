namespace StemCode.CLI;

internal static class ExitCodeMapper
{
    public const int Success = 0;
    public const int Error = 1;
    public const int UsageError = 2;
    public const int Cancelled = 130;
}
