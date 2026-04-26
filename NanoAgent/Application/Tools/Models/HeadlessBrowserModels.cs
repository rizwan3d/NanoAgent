namespace NanoAgent.Application.Tools.Models;

public sealed record HeadlessBrowserRequest(
    string Url,
    string ResponseLength,
    int ViewportWidth,
    int ViewportHeight,
    int WaitMilliseconds,
    int TimeoutMilliseconds,
    bool CaptureScreenshot,
    bool IncludeHtml,
    string? UserAgent = null);

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
    long ByteCount,
    int ViewportWidth,
    int ViewportHeight);
