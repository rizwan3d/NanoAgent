using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Infrastructure.Tools;

internal sealed partial class HeadlessBrowserService : IHeadlessBrowserService
{
    private const int MaxLineLength = 240;
    private const int BrowserErrorPreviewLength = 1200;
    private static readonly TimeSpan ArtifactDirectoryMaxAge = TimeSpan.FromHours(24);

    private readonly string? _browserExecutablePath;
    private readonly IProcessRunner _processRunner;

    public HeadlessBrowserService(IProcessRunner processRunner)
        : this(processRunner, browserExecutablePath: null)
    {
    }

    internal HeadlessBrowserService(
        IProcessRunner processRunner,
        string? browserExecutablePath)
    {
        _processRunner = processRunner;
        _browserExecutablePath = browserExecutablePath;
    }

    public async Task<HeadlessBrowserResult> RunAsync(
        HeadlessBrowserRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        string browserExecutable = ResolveBrowserExecutable();
        List<string> warnings = [];
        string artifactDirectory = GetArtifactDirectory(sessionId);
        string userDataDirectory = CreateUserDataDirectory(artifactDirectory);

        try
        {
            string html = await DumpDomAsync(
                browserExecutable,
                userDataDirectory,
                request,
                warnings,
                cancellationToken);

            string? title = ExtractTitle(html);
            string text = LimitText(
                ExtractVisibleText(html),
                GetTextLimit(request.ResponseLength));
            string? includedHtml = request.IncludeHtml
                ? LimitText(html, GetHtmlLimit(request.ResponseLength))
                : null;

            HeadlessBrowserScreenshotResult? screenshot = request.CaptureScreenshot
                ? await CaptureScreenshotAsync(
                    browserExecutable,
                    userDataDirectory,
                    artifactDirectory,
                    request,
                    warnings,
                    cancellationToken)
                : null;

            return new HeadlessBrowserResult(
                GetBrowserDisplayName(browserExecutable),
                request.Url,
                title,
                text,
                text.Length,
                includedHtml,
                html.Length,
                screenshot,
                warnings);
        }
        finally
        {
            TryDeleteDirectory(userDataDirectory);
        }
    }

    private async Task<string> DumpDomAsync(
        string browserExecutable,
        string userDataDirectory,
        HeadlessBrowserRequest request,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        BrowserRunResult result = await RunBrowserCommandWithHeadlessFallbackAsync(
            browserExecutable,
            CreateDomArguments,
            userDataDirectory,
            request,
            GetHtmlCaptureLimit(request.ResponseLength),
            warnings,
            cancellationToken);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput;
        }

        throw new InvalidOperationException(
            $"Headless browser did not return rendered DOM. {BuildFailureDetails(result)}");
    }

    private async Task<HeadlessBrowserScreenshotResult> CaptureScreenshotAsync(
        string browserExecutable,
        string userDataDirectory,
        string artifactDirectory,
        HeadlessBrowserRequest request,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        string screenshotPath = Path.Combine(
            artifactDirectory,
            "screenshot-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) +
            "-" +
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) +
            ".png");

        BrowserRunResult result = await RunBrowserCommandWithHeadlessFallbackAsync(
            browserExecutable,
            (headlessArgument, userDataDir, browserRequest) =>
                CreateScreenshotArguments(headlessArgument, userDataDir, browserRequest, screenshotPath),
            userDataDirectory,
            request,
            maxOutputCharacters: 4000,
            warnings,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Headless browser screenshot failed. {BuildFailureDetails(result)}");
        }

        if (!File.Exists(screenshotPath))
        {
            throw new InvalidOperationException("Headless browser completed but did not create the screenshot file.");
        }

        FileInfo fileInfo = new(screenshotPath);
        if (fileInfo.Length == 0)
        {
            warnings.Add("Screenshot file was created but it is empty.");
        }

        Directory.SetLastWriteTimeUtc(artifactDirectory, DateTime.UtcNow);

        return new HeadlessBrowserScreenshotResult(
            screenshotPath,
            artifactDirectory,
            request.ScreenshotRetention,
            fileInfo.Length,
            request.ViewportWidth,
            request.ViewportHeight);
    }

    private async Task<BrowserRunResult> RunBrowserCommandWithHeadlessFallbackAsync(
        string browserExecutable,
        Func<string, string, HeadlessBrowserRequest, IReadOnlyList<string>> argumentsFactory,
        string userDataDirectory,
        HeadlessBrowserRequest request,
        int maxOutputCharacters,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        BrowserRunResult firstResult = await RunBrowserCommandAsync(
            browserExecutable,
            argumentsFactory("--headless=new", userDataDirectory, request),
            request,
            maxOutputCharacters,
            cancellationToken);

        if (firstResult.ExitCode == 0)
        {
            return firstResult;
        }

        BrowserRunResult fallbackResult = await RunBrowserCommandAsync(
            browserExecutable,
            argumentsFactory("--headless", userDataDirectory, request),
            request,
            maxOutputCharacters,
            cancellationToken);

        if (fallbackResult.ExitCode == 0)
        {
            warnings.Add("Browser did not accept '--headless=new'; retried with '--headless'.");
        }

        return fallbackResult;
    }

    private async Task<BrowserRunResult> RunBrowserCommandAsync(
        string browserExecutable,
        IReadOnlyList<string> arguments,
        HeadlessBrowserRequest request,
        int maxOutputCharacters,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(request.TimeoutMilliseconds));

        try
        {
            ProcessExecutionResult result = await _processRunner.RunAsync(
                new ProcessExecutionRequest(
                    browserExecutable,
                    arguments,
                    MaxOutputCharacters: maxOutputCharacters),
                timeoutSource.Token);

            return new BrowserRunResult(
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Headless browser timed out after {request.TimeoutMilliseconds} ms.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Unable to start browser executable '{browserExecutable}': {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<string> CreateDomArguments(
        string headlessArgument,
        string userDataDirectory,
        HeadlessBrowserRequest request)
    {
        return
        [
            .. CreateBaseArguments(headlessArgument, userDataDirectory, request),
            "--dump-dom",
            request.Url
        ];
    }

    private static IReadOnlyList<string> CreateScreenshotArguments(
        string headlessArgument,
        string userDataDirectory,
        HeadlessBrowserRequest request,
        string screenshotPath)
    {
        return
        [
            .. CreateBaseArguments(headlessArgument, userDataDirectory, request),
            $"--screenshot={screenshotPath}",
            request.Url
        ];
    }

    private static IReadOnlyList<string> CreateBaseArguments(
        string headlessArgument,
        string userDataDirectory,
        HeadlessBrowserRequest request)
    {
        List<string> arguments =
        [
            headlessArgument,
            "--disable-gpu",
            "--disable-extensions",
            "--disable-dev-shm-usage",
            "--hide-scrollbars",
            "--mute-audio",
            "--no-default-browser-check",
            "--no-first-run",
            $"--user-data-dir={userDataDirectory}",
            $"--virtual-time-budget={request.WaitMilliseconds}",
            $"--window-size={request.ViewportWidth},{request.ViewportHeight}"
        ];

        if (!string.IsNullOrWhiteSpace(request.UserAgent))
        {
            arguments.Add($"--user-agent={request.UserAgent}");
        }

        return arguments;
    }

    private string ResolveBrowserExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_browserExecutablePath))
        {
            return _browserExecutablePath!;
        }

        foreach (string candidate in GetBrowserCandidates())
        {
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
            {
                return candidate;
            }

            string? pathCandidate = FindOnPath(candidate);
            if (!string.IsNullOrWhiteSpace(pathCandidate))
            {
                return pathCandidate!;
            }
        }

        throw new InvalidOperationException(
            "No supported headless browser was found. Install Microsoft Edge, Google Chrome, or Chromium, or put one of their executables on PATH.");
    }

    private static IReadOnlyList<string> GetBrowserCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft",
                    "Edge",
                    "Application",
                    "msedge.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft",
                    "Edge",
                    "Application",
                    "msedge.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Google",
                    "Chrome",
                    "Application",
                    "chrome.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Google",
                    "Chrome",
                    "Application",
                    "chrome.exe"),
                "msedge.exe",
                "chrome.exe",
                "chromium.exe"
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
                "microsoft-edge",
                "google-chrome",
                "chromium"
            ];
        }

        return
        [
            "/usr/bin/microsoft-edge",
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "microsoft-edge",
            "google-chrome",
            "google-chrome-stable",
            "chromium",
            "chromium-browser"
        ];
    }

    private static string? FindOnPath(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory, fileName);
                if (!candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    candidate += extension;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string GetArtifactDirectory(string sessionId)
    {
        string rootDirectory = GetArtifactRootDirectory();
        CleanupExpiredArtifactDirectories(rootDirectory);

        string directory = Path.Combine(
            rootDirectory,
            SanitizePathSegment(sessionId));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetArtifactRootDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "NanoAgent",
            "browser");
    }

    private static string CreateUserDataDirectory(string artifactDirectory)
    {
        string directory = Path.Combine(
            artifactDirectory,
            "profile-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        Directory.CreateDirectory(directory);
        Directory.SetLastWriteTimeUtc(artifactDirectory, DateTime.UtcNow);
        return directory;
    }

    private static void CleanupExpiredArtifactDirectories(string rootDirectory)
    {
        try
        {
            if (!Directory.Exists(rootDirectory))
            {
                return;
            }

            DateTime expiredBeforeUtc = DateTime.UtcNow - ArtifactDirectoryMaxAge;
            foreach (string directory in Directory.EnumerateDirectories(rootDirectory))
            {
                if (Directory.GetLastWriteTimeUtc(directory) < expiredBeforeUtc)
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? "session"
            : sanitized;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetBrowserDisplayName(string browserExecutable)
    {
        return Path.GetFileName(browserExecutable);
    }

    private static string? ExtractTitle(string html)
    {
        Match match = HtmlTitleRegex().Match(html);
        return match.Success
            ? CleanupHtmlText(match.Groups["title"].Value)
            : null;
    }

    private static string ExtractVisibleText(string html)
    {
        Match bodyMatch = HtmlBodyRegex().Match(html);
        string bodyOrDocument = bodyMatch.Success
            ? bodyMatch.Groups["body"].Value
            : html;

        string withoutScripts = HtmlScriptRegex().Replace(bodyOrDocument, Environment.NewLine);
        string withoutStyles = HtmlStyleRegex().Replace(withoutScripts, Environment.NewLine);
        string withLineBreaks = HtmlBlockBreakRegex().Replace(withoutStyles, Environment.NewLine);
        string withoutTags = HtmlTagRegex().Replace(withLineBreaks, string.Empty);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return string.Join(Environment.NewLine, SplitLines(decoded));
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        string normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        List<string> lines = [];
        foreach (string line in normalized.Split('\n', StringSplitOptions.None))
        {
            string collapsed = WhitespaceRegex().Replace(line, " ").Trim();
            if (!string.IsNullOrWhiteSpace(collapsed))
            {
                lines.Add(collapsed.Length > MaxLineLength
                    ? collapsed[..MaxLineLength]
                    : collapsed);
            }
        }

        return lines;
    }

    private static string CleanupHtmlText(string value)
    {
        string withoutTags = HtmlTagRegex().Replace(value, string.Empty);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string LimitText(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }

        return limit <= 3
            ? value[..limit]
            : value[..(limit - 3)] + "...";
    }

    private static int GetHtmlCaptureLimit(string responseLength)
    {
        return responseLength switch
        {
            "short" => 24_000,
            "long" => 160_000,
            _ => 80_000
        };
    }

    private static int GetHtmlLimit(string responseLength)
    {
        return responseLength switch
        {
            "short" => 8_000,
            "long" => 80_000,
            _ => 24_000
        };
    }

    private static int GetTextLimit(string responseLength)
    {
        return responseLength switch
        {
            "short" => 4_000,
            "long" => 24_000,
            _ => 12_000
        };
    }

    private static string BuildFailureDetails(BrowserRunResult result)
    {
        string output = !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError
            : result.StandardOutput;

        string details = string.IsNullOrWhiteSpace(output)
            ? "No browser output was captured."
            : LimitText(output.Trim(), BrowserErrorPreviewLength);

        return $"Exit code: {result.ExitCode}. {details}";
    }

    [GeneratedRegex(
        "<title[^>]*>(?<title>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTitleRegex();

    [GeneratedRegex(
        "<body[^>]*>(?<body>.*?)</body>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBodyRegex();

    [GeneratedRegex(
        "<script\\b.*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlScriptRegex();

    [GeneratedRegex(
        "<style\\b.*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlStyleRegex();

    [GeneratedRegex(
        "</?(?:p|br|div|li|tr|h1|h2|h3|h4|h5|h6|section|article|header|footer|main|aside|ul|ol|table|button|label|input|textarea|select|option)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBlockBreakRegex();

    [GeneratedRegex(
        "<.*?>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(
        "\\s+",
        RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record BrowserRunResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
