namespace StemCode.Infrastructure.Storage;

internal static class FilePermissionHelper
{
    public static void EnsurePrivateDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            directoryPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    public static void EnsurePrivateFile(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            filePath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);
    }
}
