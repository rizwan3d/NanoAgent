namespace NanoAgent.Application.Tools.Models;

public static class HeadlessBrowserScreenshotRetention
{
    public const string Turn = "turn";
    public const string Session = "session";
    public const string Keep = "keep";

    public static bool IsSupported(string value)
    {
        return string.Equals(value, Turn, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Session, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Keep, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record HeadlessBrowserRequest(
    string Url,
    string ResponseLength,
    int ViewportWidth,
    int ViewportHeight,
    int WaitMilliseconds,
    int TimeoutMilliseconds,
    bool CaptureScreenshot,
    bool IncludeHtml,
    string? UserAgent = null,
    string ScreenshotRetention = HeadlessBrowserScreenshotRetention.Session);

public sealed record HeadlessBrowserResult(
    string Browser,
    string Url,
    string? Title,
    string Text,
    int TextCharacterCount,
    string? Html,
    int HtmlCharacterCount,
    HeadlessBrowserScreenshotResult? Screenshot,
    IReadOnlyList<string> Warnings);

public sealed record HeadlessBrowserScreenshotResult(
    string Path,
    string ArtifactDirectory,
    string Retention,
    long ByteCount,
    int ViewportWidth,
    int ViewportHeight);
