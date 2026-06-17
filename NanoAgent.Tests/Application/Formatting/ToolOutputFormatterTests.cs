using FluentAssertions;
using NanoAgent.Application.Formatting;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Formatting;

public sealed class ToolOutputFormatterTests
{
    private readonly ToolOutputFormatter _sut = new();

    [Fact]
    public void DescribeCall_ShouldSummarizeKnownToolArguments()
    {
        ConversationToolCall shellCall = new(
            "call-1",
            "shell_command",
            """
            { "command": "  git status  " }
            """);
        ConversationToolCall webCall = new(
            "call-2",
            "web_search",
            """
            { "search_query": [{ "q": "nanoagent coverage" }] }
            """);
        ConversationToolCall orchestrateCall = new(
            "call-3",
            "agent_orchestrate",
            """
            { "tasks": [{}, {}] }
            """);

        _sut.DescribeCall(shellCall).Should().Be("command: git status");
        _sut.DescribeCall(webCall).Should().Be("web search: \"nanoagent coverage\"");
        _sut.DescribeCall(orchestrateCall).Should().Be("subagent orchestration: 2 tasks");
    }

    [Fact]
    public void FormatCallPreview_ShouldShowStructuredPreview_ForSavedFileWriteCall()
    {
        ConversationToolCall toolCall = new(
            "call-1",
            "file_write",
            """
            {
              "path": "src/app.txt",
              "overwrite": true,
              "content": "alpha\nbeta\ngamma"
            }
            """);

        string preview = _sut.FormatCallPreview(toolCall);

        preview.Should().Contain("Previewed saved tool call: file write: src/app.txt");
        preview.Should().Contain("result output was not stored in this older section");
        preview.Should().Contain("path: src/app.txt");
        preview.Should().Contain("overwrite: true");
        preview.Should().Contain("content: 16 chars");
        preview.Should().Contain("1 alpha");
        preview.Should().Contain("2 beta");
        preview.Should().Contain("3 gamma");
    }

    [Fact]
    public void FormatCallPreview_ShouldFallBackToArgumentsPreview_WhenArgumentsJsonIsInvalid()
    {
        ConversationToolCall toolCall = new(
            "call-1",
            "custom_tool",
            "{ invalid");

        string preview = _sut.FormatCallPreview(toolCall);

        preview.Should().Contain("Previewed saved tool call: custom_tool");
        preview.Should().Contain("arguments: { invalid");
    }

    [Fact]
    public void FormatResults_ShouldGroupFileEdits_AndSkipSuccessfulPlanUpdate()
    {
        ToolExecutionBatchResult batch = new(
        [
            CreateResult(
                "update_plan",
                """
                { "Items": [{ "Step": "Write tests", "Status": "completed" }] }
                """),
            CreateResult(
                "file_write",
                """
                {
                  "Path": "src/new-file.cs",
                  "AddedLineCount": 3,
                  "RemovedLineCount": 1,
                  "PreviewLines": [
                    { "LineNumber": 10, "Kind": "add", "Text": "var answer = 42;" },
                    { "LineNumber": 11, "Kind": "remove", "Text": "return 0;" }
                  ],
                  "RemainingPreviewLineCount": 1
                }
                """),
            CreateResult(
                "file_delete",
                """
                {
                  "Path": "src/renamed.cs",
                  "PreviousPath": "src/old.cs",
                  "AddedLineCount": 0,
                  "RemovedLineCount": 2,
                  "PreviewLines": [
                    { "LineNumber": 4, "Kind": "remove", "Text": "legacy();" }
                  ]
                }
                """)
        ]);

        IReadOnlyList<string> messages = _sut.FormatResults(batch);

        messages.Should().HaveCount(1);
        messages[0].Should().Contain("Edited 2 files (+3 -3)");
        messages[0].Should().Contain("src/new-file.cs (+3 -1)");
        messages[0].Should().Contain("10 +var answer = 42;");
        messages[0].Should().Contain("11 -return 0;");
        messages[0].Should().Contain("... +1 lines");
        messages[0].Should().Contain("src/old.cs -> src/renamed.cs (+0 -2)");
    }

    [Fact]
    public void FormatResults_ShouldSummarizeShellCommandOutput_UsingStandardErrorWhenPresent()
    {
        ToolExecutionBatchResult batch = new(
        [
            CreateResult(
                "shell_command",
                """
                {
                  "Command": "dotnet test",
                  "WorkingDirectory": "/repo",
                  "ExitCode": 2,
                  "StandardOutput": "build ok",
                  "StandardError": "failed line 1\nfailed line 2"
                }
                """)
        ]);

        IReadOnlyList<string> messages = _sut.FormatResults(batch);

        messages.Should().ContainSingle();
        messages[0].Should().Contain("Ran dotnet test (exit 2)");
        messages[0].Should().Contain("stderr:");
        messages[0].Should().Contain("failed line 1");
        messages[0].Should().Contain("failed line 2");
    }

    [Fact]
    public void FormatResults_ShouldRenderCompleteShellOutput_WhenFullToolOutputIsEnabled()
    {
        bool? previousOverride = ToolOutputDisplay.FullToolOutputOverride;
        bool? previousProfile = ToolOutputDisplay.ProfileFullToolOutput;
        try
        {
            ToolOutputDisplay.FullToolOutputOverride = true;
            ToolOutputDisplay.ProfileFullToolOutput = null;

            string output = string.Join("\\n", Enumerable.Range(1, 10).Select(static line => $"out {line}"));
            ToolExecutionBatchResult batch = new(
            [
                CreateResult(
                    "shell_command",
                    $$"""
                    {
                      "Command": "ls",
                      "WorkingDirectory": "/repo",
                      "ExitCode": 0,
                      "StandardOutput": "{{output}}",
                      "StandardError": ""
                    }
                    """)
            ]);

            IReadOnlyList<string> messages = _sut.FormatResults(batch);

            messages.Should().ContainSingle();
            messages[0].Should().Contain("out 1");
            messages[0].Should().Contain("out 10");
            messages[0].Should().NotContain("... +");
        }
        finally
        {
            ToolOutputDisplay.FullToolOutputOverride = previousOverride;
            ToolOutputDisplay.ProfileFullToolOutput = previousProfile;
        }
    }

    [Fact]
    public void FormatResults_ShouldBuildPreviews_ForFileReadDirectorySearchAndTextSearch()
    {
        ToolExecutionBatchResult batch = new(
        [
            CreateResult(
                "file_read",
                """
                {
                  "Path": "README.md",
                  "Content": "line 1\nline 2",
                  "CharacterCount": 13
                }
                """),
            CreateResult(
                "directory_list",
                """
                {
                  "Path": "src",
                  "Entries": [
                    { "Path": "src/Program.cs", "EntryType": "file" },
                    { "Path": "src/Models", "EntryType": "directory" }
                  ]
                }
                """),
            CreateResult(
                "search_files",
                """
                {
                  "Query": "Program",
                  "Path": "src",
                  "Matches": [
                    { "Path": "src/Program.cs", "Score": 9000, "MatchKind": "filename_contains" },
                    { "Path": "src/Program.Tests.cs", "Score": 8500, "MatchKind": "path_contains" }
                  ],
                  "Glob": "**/*.cs",
                  "Mode": "fuzzy",
                  "Fuzzy": true,
                  "WholeWord": false,
                  "Limit": 5,
                  "Offset": 0,
                  "HasMore": true,
                  "NextCursor": "Mg==",
                  "TotalMatchCount": 3,
                  "CaseSensitive": false
                }
                """),
            CreateResult(
                "text_search",
                """
                {
                  "Query": "coverage",
                  "Path": "src",
                  "Matches": [
                    { "Path": "src/Program.cs", "LineNumber": 42, "LineText": "coverage threshold" }
                  ]
                }
                """)
        ]);

        IReadOnlyList<string> messages = _sut.FormatResults(batch);

        messages.Should().HaveCount(4);
        messages[0].Should().Contain("Read README.md (13 chars)");
        messages[0].Should().Contain("1 line 1");
        messages[1].Should().Contain("Listed src (2 entries)");
        messages[1].Should().Contain("file: src/Program.cs");
        messages[1].Should().Contain("directory: src/Models");
        messages[2].Should().Contain("Found 2 files for \"Program\" in src (limit 5, offset 0, mode fuzzy, caseSensitive false, wholeWord false, glob **/*.cs, hasMore true)");
        messages[2].Should().Contain("src/Program.cs (score 9000, filename_contains)");
        messages[2].Should().Contain("nextCursor: Mg==");
        messages[2].Should().Contain("total matches: 3");
        messages[3].Should().Contain("Searched src for \"coverage\" (1 match)");
        messages[3].Should().Contain("src/Program.cs:42 coverage threshold");
    }

    [Fact]
    public void FormatResults_ShouldTruncateFileReadPreview_WhenFullToolOutputIsDisabled()
    {
        bool? previousOverride = ToolOutputDisplay.FullToolOutputOverride;
        bool? previousProfile = ToolOutputDisplay.ProfileFullToolOutput;
        try
        {
            ToolOutputDisplay.FullToolOutputOverride = false;
            ToolOutputDisplay.ProfileFullToolOutput = null;
            ToolExecutionBatchResult batch = CreateTenLineFileReadBatch();

            IReadOnlyList<string> messages = _sut.FormatResults(batch);

            messages.Should().ContainSingle();
            messages[0].Should().Contain("  - preview:");
            messages[0].Should().Contain("1 line 1");
            messages[0].Should().Contain("8 line 8");
            messages[0].Should().NotContain("9 line 9");
            messages[0].Should().Contain("... +2 lines");
        }
        finally
        {
            ToolOutputDisplay.FullToolOutputOverride = previousOverride;
            ToolOutputDisplay.ProfileFullToolOutput = previousProfile;
        }
    }

    [Fact]
    public void FormatResults_ShouldRenderCompleteFile_WhenCommandOverrideEnablesFullOutput()
    {
        bool? previousOverride = ToolOutputDisplay.FullToolOutputOverride;
        bool? previousProfile = ToolOutputDisplay.ProfileFullToolOutput;
        try
        {
            ToolOutputDisplay.FullToolOutputOverride = true;
            ToolOutputDisplay.ProfileFullToolOutput = null;
            ToolExecutionBatchResult batch = CreateTenLineFileReadBatch();

            IReadOnlyList<string> messages = _sut.FormatResults(batch);

            messages.Should().ContainSingle();
            messages[0].Should().Contain("  - content:");
            messages[0].Should().Contain("1 line 1");
            messages[0].Should().Contain("9 line 9");
            messages[0].Should().Contain("10 line 10");
            messages[0].Should().NotContain("... +");
        }
        finally
        {
            ToolOutputDisplay.FullToolOutputOverride = previousOverride;
            ToolOutputDisplay.ProfileFullToolOutput = previousProfile;
        }
    }

    [Fact]
    public void FormatResults_ShouldRenderCompleteFile_WhenActiveProfilePrefersFullOutput()
    {
        bool? previousOverride = ToolOutputDisplay.FullToolOutputOverride;
        bool? previousProfile = ToolOutputDisplay.ProfileFullToolOutput;
        try
        {
            // No command override; the active profile preference should win over the default.
            ToolOutputDisplay.FullToolOutputOverride = null;
            ToolOutputDisplay.ProfileFullToolOutput = true;
            ToolExecutionBatchResult batch = CreateTenLineFileReadBatch();

            IReadOnlyList<string> messages = _sut.FormatResults(batch);

            messages.Should().ContainSingle();
            messages[0].Should().Contain("  - content:");
            messages[0].Should().Contain("10 line 10");
            messages[0].Should().NotContain("... +");
        }
        finally
        {
            ToolOutputDisplay.FullToolOutputOverride = previousOverride;
            ToolOutputDisplay.ProfileFullToolOutput = previousProfile;
        }
    }

    [Fact]
    public void FormatResults_ShouldLetCommandOverrideWinOverProfilePreference()
    {
        bool? previousOverride = ToolOutputDisplay.FullToolOutputOverride;
        bool? previousProfile = ToolOutputDisplay.ProfileFullToolOutput;
        try
        {
            ToolOutputDisplay.FullToolOutputOverride = false;
            ToolOutputDisplay.ProfileFullToolOutput = true;
            ToolExecutionBatchResult batch = CreateTenLineFileReadBatch();

            IReadOnlyList<string> messages = _sut.FormatResults(batch);

            messages.Should().ContainSingle();
            messages[0].Should().Contain("  - preview:");
            messages[0].Should().NotContain("9 line 9");
            messages[0].Should().Contain("... +2 lines");
        }
        finally
        {
            ToolOutputDisplay.FullToolOutputOverride = previousOverride;
            ToolOutputDisplay.ProfileFullToolOutput = previousProfile;
        }
    }

    [Fact]
    public void FormatResults_ShouldSummarizeWebSearchSearchesAndWarnings()
    {
        ToolExecutionBatchResult batch = new(
        [
            CreateResult(
                "web_search",
                """
                {
                  "SearchQuery": [
                    {
                      "Query": "nanoagent",
                      "Content": "Title: NanoAgent\nURL: https://example.test/nanoagent",
                      "Results": [
                        {
                          "Title": "NanoAgent",
                          "Url": "https://example.test/nanoagent"
                        }
                      ],
                      "Warning": "slow response"
                    }
                  ],
                  "Warnings": ["global warning"]
                }
                """)
        ]);

        IReadOnlyList<string> messages = _sut.FormatResults(batch);

        messages.Should().ContainSingle();
        messages[0].Should().Contain("web_search completed (1 search)");
        messages[0].Should().Contain("search \"nanoagent\": 1 result");
        messages[0].Should().Contain("NanoAgent - https://example.test/nanoagent");
        messages[0].Should().Contain("warning: slow response");
        messages[0].Should().Contain("warning: global warning");
    }

    [Fact]
    public void FormatResults_ShouldUseRenderPayload_ForFailedTools()
    {
        ToolExecutionBatchResult batch = new(
        [
            CreateResult(
                "custom_tool",
                "{}",
                status: ToolResultStatus.ExecutionError,
                message: "tool failed",
                renderPayload: new ToolRenderPayload("Title", "Rendered text"))
        ]);

        IReadOnlyList<string> messages = _sut.FormatResults(batch);

        messages.Should().ContainSingle();
        messages[0].Should().Be($"Tool issue: Title{Environment.NewLine}{Environment.NewLine}Rendered text");
    }

    private static ToolExecutionBatchResult CreateTenLineFileReadBatch()
    {
        string content = string.Join("\\n", Enumerable.Range(1, 10).Select(static line => $"line {line}"));
        return new ToolExecutionBatchResult(
        [
            CreateResult(
                "file_read",
                $$"""
                {
                  "Path": "README.md",
                  "Content": "{{content}}",
                  "CharacterCount": {{content.Replace("\\n", "\n", StringComparison.Ordinal).Length}}
                }
                """)
        ]);
    }

    private static ToolInvocationResult CreateResult(
        string toolName,
        string jsonResult,
        ToolResultStatus status = ToolResultStatus.Success,
        string message = "ok",
        ToolRenderPayload? renderPayload = null)
    {
        ToolResult result = status switch
        {
            ToolResultStatus.Success => ToolResult.Success(message, jsonResult, renderPayload),
            ToolResultStatus.InvalidArguments => ToolResult.InvalidArguments(message, jsonResult, renderPayload),
            ToolResultStatus.NotFound => ToolResult.NotFound(message, jsonResult, renderPayload),
            ToolResultStatus.PermissionDenied => ToolResult.PermissionDenied(message, jsonResult, renderPayload),
            _ => ToolResult.ExecutionError(message, jsonResult, renderPayload)
        };

        return new ToolInvocationResult(Guid.NewGuid().ToString("N"), toolName, result);
    }
}
