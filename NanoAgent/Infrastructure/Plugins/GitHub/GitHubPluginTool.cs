using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Serialization;

namespace NanoAgent.Infrastructure.Plugins.GitHub;

internal sealed class GitHubPluginTool : ITool
{
    public const string PluginName = "github";
    private const int MaxRenderTextLength = 4000;
    private const string RepositoryArgumentName = "repository";
    private const string NumberArgumentName = "number";
    private const string DefaultApiBaseUrl = "https://api.github.com";
    private static readonly string[] DefaultTokenEnvironmentVariables = ["GITHUB_TOKEN", "GH_TOKEN"];

    private readonly PluginConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubPluginToolKind _kind;
    private readonly string _toolName;

    public GitHubPluginTool(
        PluginConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        GitHubPluginToolKind kind)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _kind = kind;
        _toolName = GetToolName(kind);
    }

    public string Description => _kind switch
    {
        GitHubPluginToolKind.Repository => "Read public or authenticated GitHub repository metadata by owner/name.",
        GitHubPluginToolKind.Issue => "Read a GitHub issue by repository and issue number.",
        GitHubPluginToolKind.PullRequest => "Read a GitHub pull request by repository and pull request number.",
        _ => "Read GitHub data."
    };

    public string Name => PluginToolName.Create(PluginName, _toolName);

    public string PermissionRequirements => PluginJson.CreatePermissionRequirements(
        PluginName,
        _toolName,
        _configuration.GetApprovalMode(_toolName),
        RepositoryArgumentName);

    public string Schema => _kind switch
    {
        GitHubPluginToolKind.Repository => """
            {
              "type": "object",
              "properties": {
                "repository": {
                  "type": "string",
                  "description": "GitHub repository in owner/name form."
                }
              },
              "required": ["repository"],
              "additionalProperties": false
            }
            """,
        GitHubPluginToolKind.Issue => """
            {
              "type": "object",
              "properties": {
                "repository": {
                  "type": "string",
                  "description": "GitHub repository in owner/name form."
                },
                "number": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "Issue number."
                }
              },
              "required": ["repository", "number"],
              "additionalProperties": false
            }
            """,
        GitHubPluginToolKind.PullRequest => """
            {
              "type": "object",
              "properties": {
                "repository": {
                  "type": "string",
                  "description": "GitHub repository in owner/name form."
                },
                "number": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "Pull request number."
                }
              },
              "required": ["repository", "number"],
              "additionalProperties": false
            }
            """,
        _ => """{ "type": "object", "properties": {}, "additionalProperties": false }"""
    };

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetRepository(context.Arguments, out string owner, out string repository, out ToolResult? repositoryError))
        {
            return repositoryError!;
        }

        int number = 0;
        if (_kind is GitHubPluginToolKind.Issue or GitHubPluginToolKind.PullRequest &&
            !TryGetPositiveNumber(context.Arguments, out number, out ToolResult? numberError))
        {
            return numberError!;
        }

        Uri requestUri = CreateRequestUri(owner, repository, number);
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        AddAuthorizationHeader(request);

        HttpResponseMessage response;
        string responseText;
        try
        {
            response = await _httpClientFactory
                .CreateClient("NanoAgent.Plugins.GitHub")
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ToolResultFactory.ExecutionError(
                "github_request_failed",
                $"GitHub request failed: {exception.Message}",
                new ToolRenderPayload(
                    "GitHub request failed",
                    exception.Message));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return CreateHttpErrorResult(response.StatusCode, responseText);
            }

            if (!TryParseJsonObject(responseText, out JsonElement payload))
            {
                return ToolResultFactory.ExecutionError(
                    "github_invalid_response",
                    "GitHub returned a response that was not a JSON object.",
                    new ToolRenderPayload(
                        "GitHub response was invalid",
                        Truncate(responseText.Trim(), MaxRenderTextLength)));
            }

            return ToolResultFactory.Success(
                CreateSuccessMessage(owner, repository, number),
                payload,
                ToolJsonContext.Default.JsonElement,
                new ToolRenderPayload(
                    CreateRenderTitle(owner, repository, number),
                    CreateRenderText(payload)));
        }
    }

    private Uri CreateRequestUri(
        string owner,
        string repository,
        int number)
    {
        Uri baseUri = CreateBaseUri(_configuration.GetSetting("apiBaseUrl") ?? DefaultApiBaseUrl);
        string encodedOwner = Uri.EscapeDataString(owner);
        string encodedRepository = Uri.EscapeDataString(repository);
        string path = _kind switch
        {
            GitHubPluginToolKind.Repository => $"repos/{encodedOwner}/{encodedRepository}",
            GitHubPluginToolKind.Issue => $"repos/{encodedOwner}/{encodedRepository}/issues/{number}",
            GitHubPluginToolKind.PullRequest => $"repos/{encodedOwner}/{encodedRepository}/pulls/{number}",
            _ => throw new InvalidOperationException($"Unsupported GitHub plugin tool kind '{_kind}'.")
        };

        return new Uri(baseUri, path);
    }

    private void AddAuthorizationHeader(HttpRequestMessage request)
    {
        foreach (string environmentVariable in GetTokenEnvironmentVariables())
        {
            string? token = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            return;
        }
    }

    private IEnumerable<string> GetTokenEnvironmentVariables()
    {
        string? configuredEnvironmentVariable = _configuration.GetSetting("tokenEnvVar");
        if (!string.IsNullOrWhiteSpace(configuredEnvironmentVariable))
        {
            yield return configuredEnvironmentVariable;
            yield break;
        }

        foreach (string environmentVariable in DefaultTokenEnvironmentVariables)
        {
            yield return environmentVariable;
        }
    }

    private ToolResult CreateHttpErrorResult(
        HttpStatusCode statusCode,
        string responseText)
    {
        string message = $"GitHub returned HTTP {(int)statusCode} ({statusCode}).";
        ToolRenderPayload renderPayload = new(
            "GitHub request failed",
            string.IsNullOrWhiteSpace(responseText)
                ? message
                : Truncate(responseText.Trim(), MaxRenderTextLength));

        if (statusCode == HttpStatusCode.NotFound)
        {
            return ToolResultFactory.NotFound(
                "github_not_found",
                message,
                renderPayload);
        }

        return ToolResultFactory.ExecutionError(
            "github_http_error",
            message,
            renderPayload);
    }

    private string CreateSuccessMessage(
        string owner,
        string repository,
        int number)
    {
        string fullName = $"{owner}/{repository}";
        return _kind switch
        {
            GitHubPluginToolKind.Repository => $"Loaded GitHub repository '{fullName}'.",
            GitHubPluginToolKind.Issue => $"Loaded GitHub issue #{number} for '{fullName}'.",
            GitHubPluginToolKind.PullRequest => $"Loaded GitHub pull request #{number} for '{fullName}'.",
            _ => $"Loaded GitHub data for '{fullName}'."
        };
    }

    private string CreateRenderTitle(
        string owner,
        string repository,
        int number)
    {
        string fullName = $"{owner}/{repository}";
        return _kind switch
        {
            GitHubPluginToolKind.Repository => $"GitHub repository: {fullName}",
            GitHubPluginToolKind.Issue => $"GitHub issue: {fullName}#{number}",
            GitHubPluginToolKind.PullRequest => $"GitHub pull request: {fullName}#{number}",
            _ => $"GitHub: {fullName}"
        };
    }

    private string CreateRenderText(JsonElement payload)
    {
        List<string> lines = [];
        AddLine(lines, "Name", GetString(payload, "full_name") ?? GetString(payload, "title") ?? GetString(payload, "name"));
        AddLine(lines, "State", GetString(payload, "state"));
        AddLine(lines, "Description", GetString(payload, "description"));
        AddLine(lines, "Author", GetNestedString(payload, "user", "login"));
        AddLine(lines, "Default branch", GetString(payload, "default_branch"));
        AddLine(lines, "Head", GetNestedString(payload, "head", "ref"));
        AddLine(lines, "Base", GetNestedString(payload, "base", "ref"));
        AddLine(lines, "URL", GetString(payload, "html_url"));
        AddLine(lines, "Body", TruncateOptional(GetString(payload, "body"), 1200));

        return lines.Count == 0
            ? Truncate(payload.GetRawText(), MaxRenderTextLength)
            : string.Join(Environment.NewLine, lines);
    }

    private static void AddLine(
        List<string> lines,
        string label,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        lines.Add($"{label}: {value.Trim()}");
    }

    private static bool TryGetRepository(
        JsonElement arguments,
        out string owner,
        out string repository,
        out ToolResult? error)
    {
        owner = string.Empty;
        repository = string.Empty;
        error = null;

        if (!ToolArguments.TryGetNonEmptyString(arguments, RepositoryArgumentName, out string? value))
        {
            error = ToolResultFactory.InvalidArguments(
                "missing_repository",
                $"Tool '{PluginName}' requires a non-empty repository string in owner/name form.",
                new ToolRenderPayload(
                    "Invalid GitHub arguments",
                    "Provide repository as owner/name."));
            return false;
        }

        string[] parts = value!.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            error = ToolResultFactory.InvalidArguments(
                "invalid_repository",
                $"GitHub repository '{value}' must be in owner/name form.",
                new ToolRenderPayload(
                    "Invalid GitHub repository",
                    "Use owner/name, for example octocat/Hello-World."));
            return false;
        }

        owner = parts[0];
        repository = parts[1];
        return true;
    }

    private static bool TryGetPositiveNumber(
        JsonElement arguments,
        out int number,
        out ToolResult? error)
    {
        error = null;
        if (ToolArguments.TryGetInt32(arguments, NumberArgumentName, out number) &&
            number > 0)
        {
            return true;
        }

        error = ToolResultFactory.InvalidArguments(
            "invalid_number",
            $"Tool '{PluginName}' requires a positive integer '{NumberArgumentName}'.",
            new ToolRenderPayload(
                "Invalid GitHub arguments",
                "Provide a positive issue or pull request number."));
        return false;
    }

    private static Uri CreateBaseUri(string value)
    {
        string normalized = value.EndsWith("/", StringComparison.Ordinal)
            ? value
            : value + "/";
        return new Uri(normalized, UriKind.Absolute);
    }

    private static bool TryParseJsonObject(
        string value,
        out JsonElement payload)
    {
        payload = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            payload = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string GetToolName(GitHubPluginToolKind kind)
    {
        return kind switch
        {
            GitHubPluginToolKind.Repository => "repository",
            GitHubPluginToolKind.Issue => "issue",
            GitHubPluginToolKind.PullRequest => "pull_request",
            _ => throw new InvalidOperationException($"Unsupported GitHub plugin tool kind '{kind}'.")
        };
    }

    private static string? GetString(
        JsonElement payload,
        string propertyName)
    {
        return payload.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetNestedString(
        JsonElement payload,
        string objectPropertyName,
        string stringPropertyName)
    {
        return payload.TryGetProperty(objectPropertyName, out JsonElement objectProperty) &&
               objectProperty.ValueKind == JsonValueKind.Object &&
               objectProperty.TryGetProperty(stringPropertyName, out JsonElement stringProperty) &&
               stringProperty.ValueKind == JsonValueKind.String
            ? stringProperty.GetString()
            : null;
    }

    private static string? TruncateOptional(
        string? value,
        int maxLength)
    {
        return value is null
            ? null
            : Truncate(value, maxLength);
    }

    private static string Truncate(
        string value,
        int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength <= 3
            ? value[..maxLength]
            : value[..(maxLength - 3)] + "...";
    }
}
