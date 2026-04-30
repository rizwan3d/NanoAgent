using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Application.Tools.Serialization;
using NanoAgent.Infrastructure.Plugins;

namespace NanoAgent.Plugin.GitHub;

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

    public GitHubPluginTool(
        PluginConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        GitHubPluginToolKind kind)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(kind);

        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _kind = kind;
    }

    public string Description => _kind.Description;

    public string Name => PluginToolName.Create(PluginName, _kind.Name);

    public string PermissionRequirements => PluginJson.CreatePermissionRequirements(
        PluginName,
        _kind.Name,
        _configuration.GetApprovalMode(_kind.Name),
        _kind.PermissionArgumentName);

    public string Schema => _kind.Schema;

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateRequestUri(context.Arguments, out Uri requestUri, out string subject, out ToolResult? argumentsError))
        {
            return argumentsError!;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        AddAuthorizationHeader(request);

        HttpResponseMessage response;
        string responseText;
        try
        {
            response = await _httpClientFactory
                .CreateClient(ServiceCollectionExtensions.HttpClientName)
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

            if (!TryParseJson(responseText, out JsonElement payload))
            {
                return ToolResultFactory.ExecutionError(
                    "github_invalid_response",
                    "GitHub returned a response that was not a JSON object or array.",
                    new ToolRenderPayload(
                        "GitHub response was invalid",
                        Truncate(responseText.Trim(), MaxRenderTextLength)));
            }

            return ToolResultFactory.Success(
                $"Loaded GitHub {FormatToolName(_kind.Name)} for {subject}.",
                payload,
                ToolJsonContext.Default.JsonElement,
                new ToolRenderPayload(
                    $"GitHub {FormatToolName(_kind.Name)}",
                    CreateRenderText(payload)));
        }
    }

    private bool TryCreateRequestUri(
        JsonElement arguments,
        out Uri requestUri,
        out string subject,
        out ToolResult? error)
    {
        requestUri = default!;
        subject = string.Empty;
        error = null;

        if (_kind == GitHubPluginToolKind.SearchIssues)
        {
            return TryCreateSearchIssuesUri(arguments, out requestUri, out subject, out error);
        }

        if (_kind == GitHubPluginToolKind.SearchRepositories)
        {
            return TryCreateSearchUri("search/repositories", arguments, out requestUri, out subject, out error);
        }

        if (_kind == GitHubPluginToolKind.SearchCode)
        {
            return TryCreateSearchCodeUri(arguments, out requestUri, out subject, out error);
        }

        if (!TryGetRepository(arguments, out string owner, out string repository, out error))
        {
            return false;
        }

        string fullName = $"{owner}/{repository}";
        string encodedOwner = Uri.EscapeDataString(owner);
        string encodedRepository = Uri.EscapeDataString(repository);
        string repositoryPath = $"repos/{encodedOwner}/{encodedRepository}";
        string path;
        List<KeyValuePair<string, string?>> query = [];

        if (_kind == GitHubPluginToolKind.Repository)
        {
            path = repositoryPath;
            subject = fullName;
        }
        else if (_kind == GitHubPluginToolKind.Branch)
        {
            if (!TryGetRequiredString(arguments, "branch", "branch name", out string branch, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/branches/{Uri.EscapeDataString(branch)}";
            subject = $"{fullName}@{branch}";
        }
        else if (_kind == GitHubPluginToolKind.Commit)
        {
            if (!TryGetRequiredString(arguments, "ref", "commit SHA or ref", out string reference, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/commits/{Uri.EscapeDataString(reference)}";
            subject = $"{fullName}@{reference}";
        }
        else if (_kind == GitHubPluginToolKind.CompareRefs)
        {
            if (!TryGetRequiredString(arguments, "base", "base ref", out string baseRef, out error) ||
                !TryGetRequiredString(arguments, "head", "head ref", out string headRef, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/compare/{Uri.EscapeDataString(baseRef)}...{Uri.EscapeDataString(headRef)}";
            subject = $"{fullName} {baseRef}...{headRef}";
        }
        else if (_kind == GitHubPluginToolKind.Issue)
        {
            if (!TryGetPositiveNumber(arguments, out int number, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/issues/{number}";
            subject = $"{fullName}#{number}";
        }
        else if (_kind == GitHubPluginToolKind.IssueComments)
        {
            if (!TryGetPositiveNumber(arguments, out int number, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/issues/{number}/comments";
            subject = $"{fullName}#{number}";
        }
        else if (_kind == GitHubPluginToolKind.ListIssues)
        {
            path = $"{repositoryPath}/issues";
            subject = fullName;
            AddOptionalStringQuery(arguments, query, "state");
            AddOptionalStringQuery(arguments, query, "labels");
            AddOptionalStringQuery(arguments, query, "assignee");
            AddOptionalStringQuery(arguments, query, "since");
            AddPerPageQuery(arguments, query);
        }
        else if (_kind == GitHubPluginToolKind.PullRequest)
        {
            if (!TryGetPositiveNumber(arguments, out int number, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/pulls/{number}";
            subject = $"{fullName}#{number}";
        }
        else if (_kind == GitHubPluginToolKind.PullRequestFiles)
        {
            if (!TryGetPositiveNumber(arguments, out int number, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/pulls/{number}/files";
            subject = $"{fullName}#{number}";
        }
        else if (_kind == GitHubPluginToolKind.PullRequestReviews)
        {
            if (!TryGetPositiveNumber(arguments, out int number, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/pulls/{number}/reviews";
            subject = $"{fullName}#{number}";
        }
        else if (_kind == GitHubPluginToolKind.PullRequestReviewComments)
        {
            if (!TryGetPositiveNumber(arguments, out int number, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/pulls/{number}/comments";
            subject = $"{fullName}#{number}";
        }
        else if (_kind == GitHubPluginToolKind.ListPullRequests)
        {
            path = $"{repositoryPath}/pulls";
            subject = fullName;
            AddOptionalStringQuery(arguments, query, "state");
            AddOptionalStringQuery(arguments, query, "head");
            AddOptionalStringQuery(arguments, query, "base");
            AddOptionalStringQuery(arguments, query, "sort");
            AddOptionalStringQuery(arguments, query, "direction");
            AddPerPageQuery(arguments, query);
        }
        else if (_kind == GitHubPluginToolKind.CheckRunsForRef)
        {
            if (!TryGetRequiredString(arguments, "ref", "commit SHA or ref", out string reference, out error))
            {
                return false;
            }

            path = $"{repositoryPath}/commits/{Uri.EscapeDataString(reference)}/check-runs";
            subject = $"{fullName}@{reference}";
        }
        else if (_kind == GitHubPluginToolKind.WorkflowRuns)
        {
            path = $"{repositoryPath}/actions/runs";
            subject = fullName;
            AddOptionalStringQuery(arguments, query, "branch");
            AddOptionalStringQuery(arguments, query, "event");
            AddOptionalStringQuery(arguments, query, "status");
            AddPerPageQuery(arguments, query);
        }
        else if (_kind == GitHubPluginToolKind.LatestRelease)
        {
            path = $"{repositoryPath}/releases/latest";
            subject = fullName;
        }
        else
        {
            error = ToolResultFactory.InvalidArguments(
                "unsupported_github_tool",
                $"GitHub plugin tool '{_kind.Name}' is not supported.",
                new ToolRenderPayload(
                    "Unsupported GitHub tool",
                    $"Unsupported tool: {_kind.Name}."));
            return false;
        }

        requestUri = CreateRequestUri(path, query);
        return true;
    }

    private bool TryCreateSearchIssuesUri(
        JsonElement arguments,
        out Uri requestUri,
        out string subject,
        out ToolResult? error)
    {
        requestUri = default!;
        subject = string.Empty;

        if (!TryGetRequiredString(arguments, "query", "search query", out string query, out error))
        {
            return false;
        }

        List<string> qualifiers = [query];
        AddOptionalRepositoryQualifier(arguments, qualifiers, out error);
        if (error is not null)
        {
            return false;
        }

        AddOptionalQualifier(arguments, qualifiers, "state");
        AddOptionalQualifier(arguments, qualifiers, "type");
        List<KeyValuePair<string, string?>> queryParameters =
        [
            new("q", string.Join(' ', qualifiers))
        ];
        AddPerPageQuery(arguments, queryParameters);

        requestUri = CreateRequestUri("search/issues", queryParameters);
        subject = query;
        return true;
    }

    private bool TryCreateSearchCodeUri(
        JsonElement arguments,
        out Uri requestUri,
        out string subject,
        out ToolResult? error)
    {
        requestUri = default!;
        subject = string.Empty;

        if (!TryGetRequiredString(arguments, "query", "search query", out string query, out error))
        {
            return false;
        }

        List<string> qualifiers = [query];
        AddOptionalRepositoryQualifier(arguments, qualifiers, out error);
        if (error is not null)
        {
            return false;
        }

        AddOptionalQualifier(arguments, qualifiers, "language");
        List<KeyValuePair<string, string?>> queryParameters =
        [
            new("q", string.Join(' ', qualifiers))
        ];
        AddPerPageQuery(arguments, queryParameters);

        requestUri = CreateRequestUri("search/code", queryParameters);
        subject = query;
        return true;
    }

    private bool TryCreateSearchUri(
        string path,
        JsonElement arguments,
        out Uri requestUri,
        out string subject,
        out ToolResult? error)
    {
        requestUri = default!;
        subject = string.Empty;

        if (!TryGetRequiredString(arguments, "query", "search query", out string query, out error))
        {
            return false;
        }

        List<KeyValuePair<string, string?>> queryParameters =
        [
            new("q", query)
        ];
        AddOptionalStringQuery(arguments, queryParameters, "sort");
        AddOptionalStringQuery(arguments, queryParameters, "order");
        AddPerPageQuery(arguments, queryParameters);

        requestUri = CreateRequestUri(path, queryParameters);
        subject = query;
        return true;
    }

    private Uri CreateRequestUri(
        string path,
        IEnumerable<KeyValuePair<string, string?>> queryParameters)
    {
        Uri baseUri = CreateBaseUri(_configuration.GetSetting("apiBaseUrl") ?? DefaultApiBaseUrl);
        string query = CreateQueryString(queryParameters);
        return new Uri(baseUri, path + query);
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

    private static string CreateRenderText(JsonElement payload)
    {
        List<string> lines = [];

        if (payload.ValueKind == JsonValueKind.Array)
        {
            AddArraySummary(lines, payload);
        }
        else if (payload.ValueKind == JsonValueKind.Object)
        {
            AddObjectSummary(lines, payload);
        }

        return lines.Count == 0
            ? Truncate(payload.GetRawText(), MaxRenderTextLength)
            : Truncate(string.Join(Environment.NewLine, lines), MaxRenderTextLength);
    }

    private static void AddObjectSummary(
        List<string> lines,
        JsonElement payload)
    {
        AddLine(lines, "Name", GetString(payload, "full_name") ?? GetString(payload, "title") ?? GetString(payload, "name"));
        AddLine(lines, "State", GetString(payload, "state") ?? GetString(payload, "status") ?? GetString(payload, "conclusion"));
        AddLine(lines, "Description", GetString(payload, "description"));
        AddLine(lines, "Author", GetNestedString(payload, "user", "login") ?? GetNestedString(payload, "author", "login"));
        AddLine(lines, "Default branch", GetString(payload, "default_branch"));
        AddLine(lines, "Head", GetNestedString(payload, "head", "ref"));
        AddLine(lines, "Base", GetNestedString(payload, "base", "ref"));
        AddLine(lines, "SHA", GetString(payload, "sha"));
        AddLine(lines, "URL", GetString(payload, "html_url"));
        AddLine(lines, "Body", TruncateOptional(GetString(payload, "body"), 1200));

        AddNestedArraySummary(lines, payload, "items");
        AddNestedArraySummary(lines, payload, "workflow_runs");
        AddNestedArraySummary(lines, payload, "check_runs");
        AddNestedArraySummary(lines, payload, "commits");
        AddNestedArraySummary(lines, payload, "files");
    }

    private static void AddNestedArraySummary(
        List<string> lines,
        JsonElement payload,
        string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        if (payload.TryGetProperty("total_count", out JsonElement totalCount) &&
            totalCount.ValueKind == JsonValueKind.Number &&
            totalCount.TryGetInt32(out int total))
        {
            AddLine(lines, "Total count", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        AddLine(lines, "Collection", propertyName);
        AddArraySummary(lines, array);
    }

    private static void AddArraySummary(
        List<string> lines,
        JsonElement payload)
    {
        int count = payload.GetArrayLength();
        AddLine(lines, "Items", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (count == 0)
        {
            AddLine(lines, "Result", "No items returned.");
            return;
        }

        int index = 0;
        foreach (JsonElement item in payload.EnumerateArray())
        {
            if (index >= 10)
            {
                AddLine(lines, "More", $"{count - index} additional item(s) not shown.");
                break;
            }

            AddLine(lines, $"{index + 1}", CreateItemSummary(item));
            index++;
        }
    }

    private static string CreateItemSummary(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Truncate(item.GetRawText(), 240);
        }

        string? name =
            GetString(item, "full_name") ??
            GetString(item, "title") ??
            GetString(item, "name") ??
            GetString(item, "path") ??
            GetString(item, "filename") ??
            GetString(item, "sha");
        string? state = GetString(item, "state") ?? GetString(item, "status") ?? GetString(item, "conclusion");
        string? url = GetString(item, "html_url");

        return string.Join(
            " - ",
            new[] { name, state, url }
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim()));
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

        if (!TryParseRepository(value!, out owner, out repository))
        {
            error = ToolResultFactory.InvalidArguments(
                "invalid_repository",
                $"GitHub repository '{value}' must be in owner/name form.",
                new ToolRenderPayload(
                    "Invalid GitHub repository",
                    "Use owner/name, for example octocat/Hello-World."));
            return false;
        }

        return true;
    }

    private static bool TryParseRepository(
        string value,
        out string owner,
        out string repository)
    {
        owner = string.Empty;
        repository = string.Empty;
        string[] parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
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

    private static bool TryGetRequiredString(
        JsonElement arguments,
        string propertyName,
        string displayName,
        out string value,
        out ToolResult? error)
    {
        error = null;
        if (ToolArguments.TryGetNonEmptyString(arguments, propertyName, out string? text))
        {
            value = text!;
            return true;
        }

        value = string.Empty;
        error = ToolResultFactory.InvalidArguments(
            $"missing_{propertyName}",
            $"Tool '{PluginName}' requires a non-empty {displayName}.",
            new ToolRenderPayload(
                "Invalid GitHub arguments",
                $"Provide {displayName}."));
        return false;
    }

    private static void AddOptionalRepositoryQualifier(
        JsonElement arguments,
        List<string> qualifiers,
        out ToolResult? error)
    {
        error = null;
        if (!ToolArguments.TryGetNonEmptyString(arguments, RepositoryArgumentName, out string? repository))
        {
            return;
        }

        if (!TryParseRepository(repository!, out string owner, out string name))
        {
            error = ToolResultFactory.InvalidArguments(
                "invalid_repository",
                $"GitHub repository '{repository}' must be in owner/name form.",
                new ToolRenderPayload(
                    "Invalid GitHub repository",
                    "Use owner/name, for example octocat/Hello-World."));
            return;
        }

        qualifiers.Add($"repo:{owner}/{name}");
    }

    private static void AddOptionalQualifier(
        JsonElement arguments,
        List<string> qualifiers,
        string propertyName)
    {
        if (ToolArguments.TryGetNonEmptyString(arguments, propertyName, out string? value))
        {
            qualifiers.Add($"{propertyName}:{value}");
        }
    }

    private static void AddOptionalStringQuery(
        JsonElement arguments,
        List<KeyValuePair<string, string?>> query,
        string propertyName)
    {
        if (ToolArguments.TryGetNonEmptyString(arguments, propertyName, out string? value))
        {
            query.Add(new KeyValuePair<string, string?>(propertyName, value));
        }
    }

    private static void AddPerPageQuery(
        JsonElement arguments,
        List<KeyValuePair<string, string?>> query)
    {
        if (!ToolArguments.TryGetInt32(arguments, "per_page", out int perPage))
        {
            return;
        }

        perPage = Math.Clamp(perPage, 1, 100);
        query.Add(new KeyValuePair<string, string?>(
            "per_page",
            perPage.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static Uri CreateBaseUri(string value)
    {
        string normalized = value.EndsWith("/", StringComparison.Ordinal)
            ? value
            : value + "/";
        return new Uri(normalized, UriKind.Absolute);
    }

    private static string CreateQueryString(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        StringBuilder builder = new();
        foreach (KeyValuePair<string, string?> parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Value))
            {
                continue;
            }

            builder.Append(builder.Length == 0 ? '?' : '&');
            builder.Append(Uri.EscapeDataString(parameter.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameter.Value.Trim()));
        }

        return builder.ToString();
    }

    private static bool TryParseJson(
        string value,
        out JsonElement payload)
    {
        payload = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
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

    private static string FormatToolName(string toolName)
    {
        return toolName.Replace('_', ' ');
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
