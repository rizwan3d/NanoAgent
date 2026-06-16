using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;

namespace NanoAgent.Tests.Application.Permissions;

public sealed class PermissionRequestDisplayFormatterTests
{
    [Theory]
    [InlineData("apply_patch", "Approve patch changes?")]
    [InlineData("directory_list", "Approve directory listing?")]
    [InlineData("file_delete", "Approve file delete?")]
    [InlineData("file_read", "Approve file read?")]
    [InlineData("file_write", "Approve file write?")]
    [InlineData("headless_browser", "Approve browser request?")]
    [InlineData("search_files", "Approve file search?")]
    [InlineData("shell_command", "Approve shell command?")]
    [InlineData("text_search", "Approve text search?")]
    [InlineData("web_search", "Approve web request?")]
    [InlineData("unknown_tool", "Approve unknown_tool?")]
    public void BuildApprovalTitle_Should_ReturnExpected(string toolName, string expected)
    {
        var request = new PermissionRequestDescriptor(toolName, toolName, [toolName], []);

        string result = PermissionRequestDisplayFormatter.BuildApprovalTitle(request);

        result.Should().Be(expected);
    }

    [Fact]
    public void BuildApprovalTitle_Should_Throw_When_RequestIsNull()
    {
        Action act = () => PermissionRequestDisplayFormatter.BuildApprovalTitle(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDecisionMessage_Should_Format_With_NoSubjects()
    {
        var request = new PermissionRequestDescriptor("file_read", "read file", ["file_read"], []);
        string result = PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "granted");

        result.Should().Be("Permission granted tool 'file_read'.");
    }

    [Fact]
    public void BuildDecisionMessage_Should_Format_With_SingleSubject_For_FileRead()
    {
        var request = new PermissionRequestDescriptor("file_read", "read file", ["file_read"], ["/path/to/file.cs"]);
        string result = PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "denied");

        result.Should().Be("Permission denied tool 'file_read' to read file '/path/to/file.cs'.");
    }

    [Fact]
    public void BuildDecisionMessage_Should_Format_With_SingleSubject_For_ShellCommand()
    {
        var request = new PermissionRequestDescriptor("shell_command", "run cmd", ["shell_command"], ["dotnet build"]);
        string result = PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "granted");

        result.Should().Be("Permission granted tool 'shell_command' to run command 'dotnet build'.");
    }

    [Fact]
    public void BuildDecisionMessage_Should_Format_With_SingleSubject_For_ApplyPatch()
    {
        var request = new PermissionRequestDescriptor("apply_patch", "apply patch", ["apply_patch"], ["src/file.cs"]);
        string result = PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "granted");

        result.Should().Be("Permission granted tool 'apply_patch' to modify patch target 'src/file.cs'.");
    }

    [Fact]
    public void BuildDecisionMessage_Should_Format_With_MultipleSubjects()
    {
        var request = new PermissionRequestDescriptor("file_read", "read files", ["file_read"], ["a.cs", "b.cs"]);
        string result = PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "granted");

        result.Should().Be("Permission granted tool 'file_read' to read file for 2 targets.");
    }

    [Fact]
    public void BuildDecisionMessage_Should_Throw_When_RequestIsNull()
    {
        Action act = () => PermissionRequestDisplayFormatter.BuildDecisionMessage(null!, "granted");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDecisionMessage_Should_Throw_When_VerbIsEmpty()
    {
        var request = new PermissionRequestDescriptor("tool", "tool", ["tool"], []);
        Action act = () => PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("headless_browser", "open browser target")]
    [InlineData("web_search", "send request")]
    [InlineData("search_files", "search path")]
    [InlineData("text_search", "search text in path")]
    [InlineData("directory_list", "list path")]
    [InlineData("file_delete", "delete file")]
    [InlineData("file_write", "write file")]
    public void BuildDecisionMessage_Should_UseCorrectAction_For_ToolType(string toolName, string expectedAction)
    {
        var request = new PermissionRequestDescriptor(toolName, toolName, [toolName], ["target"]);
        string result = PermissionRequestDisplayFormatter.BuildDecisionMessage(request, "granted");

        result.Should().Be($"Permission granted tool '{toolName}' to {expectedAction} 'target'.");
    }

    [Fact]
    public void BuildPromptDescription_Should_IncludeReasonAndTool()
    {
        var descriptor = new PermissionRequestDescriptor("file_write", "write file", ["file_write"], ["/path/file.cs"]);
        var approvalRequest = new PermissionApprovalRequest(
            "agent",
            descriptor,
            "Need to write the configuration file");

        string result = PermissionRequestDisplayFormatter.BuildPromptDescription(approvalRequest);

        result.Should().Contain("Need to write the configuration file");
        result.Should().Contain("Tool: file_write");
        result.Should().Contain("File path: /path/file.cs");
    }

    [Fact]
    public void BuildPromptDescription_Should_HandleEmptySubjects()
    {
        var descriptor = new PermissionRequestDescriptor("tool", "tool", ["tool"], []);
        var approvalRequest = new PermissionApprovalRequest(
            "agent",
            descriptor,
            "Reason");

        string result = PermissionRequestDisplayFormatter.BuildPromptDescription(approvalRequest);

        result.Should().Contain("Target: this tool request");
    }

    [Fact]
    public void BuildPromptDescription_Should_Throw_When_Null()
    {
        Action act = () => PermissionRequestDisplayFormatter.BuildPromptDescription(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
