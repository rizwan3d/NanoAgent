using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Models;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Application.Tools;

internal sealed class HeadlessBrowserTool : ITool
{
    private const int DefaultViewportWidth = 1280;
    private const int DefaultViewportHeight = 720;
    private const int DefaultWaitMilliseconds = 1000;
    private const int DefaultTimeoutMilliseconds = 20000;

    private static readonly HashSet<string> AllowedResponseLengths = new(StringComparer.OrdinalIgnoreCase)
    {
        "short",
        "medium",
        "long"
    };

    private readonly IHeadlessBrowserService _headlessBrowserService;

    public HeadlessBrowserTool(IHeadlessBrowserService headlessBrowserService)
    {
        _headlessBrowserService = headlessBrowserService;
    }

    public string Description =>
        "Render a URL in an installed headless Chromium browser such as Microsoft Edge, Google Chrome, or Chromium, then return the rendered page title, visible text, optional HTML, and optional viewport screenshot metadata.";

    public string Name => AgentToolNames.HeadlessBrowser;

    public string PermissionRequirements => """
        {
          "approvalMode": "Automatic",
          "toolTags": ["webfetch", "browser"],
          "webRequest": {
            "requestArgumentName": "url"
          }
        }
        """;

    public string Schema => """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "Absolute http or https URL to render in the headless browser."
            },
            "viewport_width": {
              "type": "integer",
              "description": "Viewport width in pixels. Defaults to 1280. Minimum 320, maximum 3840."
            },
            "viewport_height": {
              "type": "integer",
              "description": "Viewport height in pixels. Defaults to 720. Minimum 240, maximum 2160."
            },
            "wait_ms": {
              "type": "integer",
              "description": "Virtual time budget in milliseconds for page scripts to settle before DOM capture. Defaults to 1000. Maximum 30000."
            },
            "timeout_ms": {
              "type": "integer",
              "description": "Maximum browser process time in milliseconds. Defaults to 20000. Maximum 25000."
            },
            "capture_screenshot": {
              "type": "boolean",
              "description": "When true, save a viewport screenshot to a temporary NanoAgent browser artifact path."
            },
            "screenshot_retention": {
              "type": "string",
              "enum": ["turn", "session", "keep"],
              "description": "Screenshot cleanup policy. Use turn to delete after the current turn, session to delete when the session closes, or keep to leave it until temp retention cleanup. Defaults to session."
            },
            "include_html": {
              "type": "boolean",
              "description": "When true, include a bounded rendered HTML excerpt in the structured result."
            },
            "user_agent": {
              "type": "string",
              "description": "Optional user agent string for the browser request."
            },
            "response_length": {
              "type": "string",
              "enum": ["short", "medium", "long"],
              "description": "Optional response density hint. Defaults to medium."
            }
          },
          "required": ["url"],
          "additionalProperties": false
        }
        """;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            HeadlessBrowserRequest request = ParseRequest(context.Arguments);
            HeadlessBrowserResult result = await _headlessBrowserService.RunAsync(
                request,
                context.Session.SessionId,
                cancellationToken);
            RegisterScreenshotCleanup(context, result);

            return ToolResultFactory.Success(
                BuildSuccessMessage(result),
                result,
                ToolJsonContext.Default.HeadlessBrowserResult,
                new ToolRenderPayload(
                    "headless_browser completed",
                    BuildRenderText(result)));
        }
        catch (ArgumentException exception)
        {
            return InvalidArguments(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ToolResultFactory.ExecutionError(
                "headless_browser_failed",
                exception.Message,
                new ToolRenderPayload(
                    "headless_browser failed",
                    exception.Message));
        }
    }

    private static HeadlessBrowserRequest ParseRequest(JsonElement arguments)
    {
        if (!ToolArguments.TryGetNonEmptyString(arguments, "url", out string? url))
        {
            throw new ArgumentException("Tool 'headless_browser' requires a non-empty 'url' string.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Property 'url' must be an absolute http or https URL.");
        }

        string responseLength = ToolArguments.GetOptionalString(arguments, "response_length") ?? "medium";
        if (!AllowedResponseLengths.Contains(responseLength))
        {
            throw new ArgumentException("Set 'response_length' to short, medium, or long.");
        }

        string screenshotRetention = ToolArguments.GetOptionalString(arguments, "screenshot_retention") ??
            HeadlessBrowserScreenshotRetention.Session;
        if (!HeadlessBrowserScreenshotRetention.IsSupported(screenshotRetention))
        {
            throw new ArgumentException("Set 'screenshot_retention' to turn, session, or keep.");
        }

        return new HeadlessBrowserRequest(
            url!,
            responseLength.ToLowerInvariant(),
            GetOptionalBoundedInt(arguments, "viewport_width", DefaultViewportWidth, 320, 3840),
            GetOptionalBoundedInt(arguments, "viewport_height", DefaultViewportHeight, 240, 2160),
            GetOptionalBoundedInt(arguments, "wait_ms", DefaultWaitMilliseconds, 0, 30000),
            GetOptionalBoundedInt(arguments, "timeout_ms", DefaultTimeoutMilliseconds, 1000, 25000),
            GetOptionalBoolean(arguments, "capture_screenshot", defaultValue: false),
            GetOptionalBoolean(arguments, "include_html", defaultValue: false),
            ToolArguments.GetOptionalString(arguments, "user_agent"),
            screenshotRetention.ToLowerInvariant());
    }

    private static int GetOptionalBoundedInt(
        JsonElement arguments,
        string propertyName,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (!arguments.TryGetProperty(propertyName, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out int value))
        {
            throw new ArgumentException($"Property '{propertyName}' must be an integer.");
        }

        if (value < minimum || value > maximum)
        {
            throw new ArgumentException(
                $"Property '{propertyName}' must be between {minimum} and {maximum}.");
        }

        return value;
    }

    private static bool GetOptionalBoolean(
        JsonElement arguments,
        string propertyName,
        bool defaultValue)
    {
        if (!arguments.TryGetProperty(propertyName, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException($"Property '{propertyName}' must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static ToolResult InvalidArguments(string message)
    {
        return ToolResultFactory.InvalidArguments(
            "invalid_headless_browser_arguments",
            message,
            new ToolRenderPayload(
                "Invalid headless_browser arguments",
                message));
    }

    private static void RegisterScreenshotCleanup(
        ToolExecutionContext context,
        HeadlessBrowserResult result)
    {
        if (result.Screenshot is null)
        {
            return;
        }

        TemporaryArtifactRetention? retention = result.Screenshot.Retention switch
        {
            HeadlessBrowserScreenshotRetention.Turn => TemporaryArtifactRetention.Turn,
            HeadlessBrowserScreenshotRetention.Session => TemporaryArtifactRetention.Session,
            _ => null
        };

        if (retention is not null)
        {
            context.Session.RegisterTemporaryArtifact(result.Screenshot.Path, retention.Value);
        }
    }

    private static string BuildSuccessMessage(HeadlessBrowserResult result)
    {
        string screenshotPart = result.Screenshot is null
            ? string.Empty
            : $" and saved a {result.Screenshot.ByteCount} byte screenshot in {result.Screenshot.ArtifactDirectory}";

        return $"headless_browser rendered '{result.Url}' with {result.TextCharacterCount} text character(s){screenshotPart}.";
    }

    private static string BuildRenderText(HeadlessBrowserResult result)
    {
        List<string> lines =
        [
            $"Browser: {result.Browser}",
            $"URL: {result.Url}",
            $"Title: {result.Title ?? "n/a"}",
            $"Text characters: {result.TextCharacterCount}"
        ];

        if (result.Screenshot is not null)
        {
            lines.Add($"Screenshot: {result.Screenshot.Path} ({result.Screenshot.ByteCount} bytes)");
            lines.Add($"Screenshot directory: {result.Screenshot.ArtifactDirectory}");
            lines.Add($"Screenshot retention: {result.Screenshot.Retention}");
        }

        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            lines.Add("Text preview:");
            lines.AddRange(GetPreviewLines(result.Text));
        }

        if (result.Warnings.Count > 0)
        {
            lines.Add("Warnings:");
            lines.AddRange(result.Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<string> GetPreviewLines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .Select(static line => line.Length > 220 ? line[..220] : line)
            .ToArray();
    }
}
