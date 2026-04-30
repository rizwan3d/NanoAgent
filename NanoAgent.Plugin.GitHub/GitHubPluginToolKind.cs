namespace NanoAgent.Plugin.GitHub;

internal sealed record GitHubPluginToolKind(
    string Name,
    string Description,
    string Schema,
    string PermissionArgumentName)
{
    public static readonly GitHubPluginToolKind Repository = new(
        "repository",
        "Read public or authenticated GitHub repository metadata by owner/name.",
        RepositorySchema,
        "repository");

    public static readonly GitHubPluginToolKind Branch = new(
        "branch",
        "Read a GitHub branch by repository and branch name.",
        BranchSchema,
        "repository");

    public static readonly GitHubPluginToolKind Commit = new(
        "commit",
        "Read a GitHub commit by repository and SHA or ref.",
        RefSchema,
        "repository");

    public static readonly GitHubPluginToolKind CompareRefs = new(
        "compare_refs",
        "Compare two refs in a GitHub repository.",
        CompareRefsSchema,
        "repository");

    public static readonly GitHubPluginToolKind Issue = new(
        "issue",
        "Read a GitHub issue by repository and issue number.",
        NumberedSchema("Issue number."),
        "repository");

    public static readonly GitHubPluginToolKind IssueComments = new(
        "issue_comments",
        "List comments for a GitHub issue.",
        NumberedSchema("Issue number."),
        "repository");

    public static readonly GitHubPluginToolKind ListIssues = new(
        "list_issues",
        "List issues in a GitHub repository with common filters.",
        ListIssuesSchema,
        "repository");

    public static readonly GitHubPluginToolKind PullRequest = new(
        "pull_request",
        "Read a GitHub pull request by repository and pull request number.",
        NumberedSchema("Pull request number."),
        "repository");

    public static readonly GitHubPluginToolKind PullRequestFiles = new(
        "pull_request_files",
        "List changed files for a GitHub pull request.",
        NumberedSchema("Pull request number."),
        "repository");

    public static readonly GitHubPluginToolKind PullRequestReviews = new(
        "pull_request_reviews",
        "List reviews for a GitHub pull request.",
        NumberedSchema("Pull request number."),
        "repository");

    public static readonly GitHubPluginToolKind PullRequestReviewComments = new(
        "pull_request_review_comments",
        "List review comments for a GitHub pull request.",
        NumberedSchema("Pull request number."),
        "repository");

    public static readonly GitHubPluginToolKind ListPullRequests = new(
        "list_pull_requests",
        "List pull requests in a GitHub repository with common filters.",
        ListPullRequestsSchema,
        "repository");

    public static readonly GitHubPluginToolKind CheckRunsForRef = new(
        "check_runs_for_ref",
        "List check runs for a GitHub commit SHA or ref.",
        RefSchema,
        "repository");

    public static readonly GitHubPluginToolKind WorkflowRuns = new(
        "workflow_runs",
        "List GitHub Actions workflow runs for a repository.",
        WorkflowRunsSchema,
        "repository");

    public static readonly GitHubPluginToolKind LatestRelease = new(
        "latest_release",
        "Read the latest GitHub release for a repository.",
        RepositorySchema,
        "repository");

    public static readonly GitHubPluginToolKind SearchIssues = new(
        "search_issues",
        "Search GitHub issues and pull requests.",
        SearchIssuesSchema,
        "query");

    public static readonly GitHubPluginToolKind SearchRepositories = new(
        "search_repositories",
        "Search GitHub repositories.",
        SearchSchema,
        "query");

    public static readonly GitHubPluginToolKind SearchCode = new(
        "search_code",
        "Search GitHub code.",
        SearchCodeSchema,
        "query");

    public static IReadOnlyList<GitHubPluginToolKind> All { get; } =
    [
        Repository,
        Branch,
        Commit,
        CompareRefs,
        Issue,
        IssueComments,
        ListIssues,
        PullRequest,
        PullRequestFiles,
        PullRequestReviews,
        PullRequestReviewComments,
        ListPullRequests,
        CheckRunsForRef,
        WorkflowRuns,
        LatestRelease,
        SearchIssues,
        SearchRepositories,
        SearchCode
    ];

    private const string RepositorySchema = """
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
        """;

    private const string BranchSchema = """
        {
          "type": "object",
          "properties": {
            "repository": {
              "type": "string",
              "description": "GitHub repository in owner/name form."
            },
            "branch": {
              "type": "string",
              "description": "Branch name."
            }
          },
          "required": ["repository", "branch"],
          "additionalProperties": false
        }
        """;

    private const string RefSchema = """
        {
          "type": "object",
          "properties": {
            "repository": {
              "type": "string",
              "description": "GitHub repository in owner/name form."
            },
            "ref": {
              "type": "string",
              "description": "Commit SHA, branch, tag, or other ref."
            }
          },
          "required": ["repository", "ref"],
          "additionalProperties": false
        }
        """;

    private const string CompareRefsSchema = """
        {
          "type": "object",
          "properties": {
            "repository": {
              "type": "string",
              "description": "GitHub repository in owner/name form."
            },
            "base": {
              "type": "string",
              "description": "Base ref."
            },
            "head": {
              "type": "string",
              "description": "Head ref."
            }
          },
          "required": ["repository", "base", "head"],
          "additionalProperties": false
        }
        """;

    private const string ListIssuesSchema = """
        {
          "type": "object",
          "properties": {
            "repository": {
              "type": "string",
              "description": "GitHub repository in owner/name form."
            },
            "state": {
              "type": "string",
              "enum": ["open", "closed", "all"],
              "description": "Issue state. Defaults to open."
            },
            "labels": {
              "type": "string",
              "description": "Comma-separated label names."
            },
            "assignee": {
              "type": "string",
              "description": "GitHub username, 'none', or '*'."
            },
            "since": {
              "type": "string",
              "description": "Only issues updated after this ISO 8601 timestamp."
            },
            "per_page": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["repository"],
          "additionalProperties": false
        }
        """;

    private const string ListPullRequestsSchema = """
        {
          "type": "object",
          "properties": {
            "repository": {
              "type": "string",
              "description": "GitHub repository in owner/name form."
            },
            "state": {
              "type": "string",
              "enum": ["open", "closed", "all"],
              "description": "Pull request state. Defaults to open."
            },
            "head": {
              "type": "string",
              "description": "Filter by head user or user:branch."
            },
            "base": {
              "type": "string",
              "description": "Filter by base branch."
            },
            "sort": {
              "type": "string",
              "enum": ["created", "updated", "popularity", "long-running"]
            },
            "direction": {
              "type": "string",
              "enum": ["asc", "desc"]
            },
            "per_page": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["repository"],
          "additionalProperties": false
        }
        """;

    private const string WorkflowRunsSchema = """
        {
          "type": "object",
          "properties": {
            "repository": {
              "type": "string",
              "description": "GitHub repository in owner/name form."
            },
            "branch": {
              "type": "string",
              "description": "Filter by branch."
            },
            "event": {
              "type": "string",
              "description": "Filter by workflow event."
            },
            "status": {
              "type": "string",
              "description": "Filter by workflow run status."
            },
            "per_page": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["repository"],
          "additionalProperties": false
        }
        """;

    private const string SearchIssuesSchema = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "GitHub issue search query."
            },
            "repository": {
              "type": "string",
              "description": "Optional repository in owner/name form to scope the search."
            },
            "state": {
              "type": "string",
              "enum": ["open", "closed"]
            },
            "type": {
              "type": "string",
              "enum": ["issue", "pr"]
            },
            "per_page": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    private const string SearchSchema = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "GitHub search query."
            },
            "sort": {
              "type": "string"
            },
            "order": {
              "type": "string",
              "enum": ["asc", "desc"]
            },
            "per_page": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    private const string SearchCodeSchema = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "GitHub code search query."
            },
            "repository": {
              "type": "string",
              "description": "Optional repository in owner/name form to scope the search."
            },
            "language": {
              "type": "string",
              "description": "Optional programming language qualifier."
            },
            "per_page": {
              "type": "integer",
              "minimum": 1,
              "maximum": 100
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    private static string NumberedSchema(string numberDescription)
    {
        return $$"""
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
                  "description": "{{numberDescription}}"
                }
              },
              "required": ["repository", "number"],
              "additionalProperties": false
            }
            """;
    }
}
