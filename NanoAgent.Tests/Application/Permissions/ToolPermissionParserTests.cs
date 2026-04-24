using NanoAgent.Application.Models;
using NanoAgent.Application.Permissions;
using FluentAssertions;

namespace NanoAgent.Tests.Application.Permissions;

public sealed class ToolPermissionParserTests
{
    [Fact]
    public void Parse_Should_ReturnNormalizedPolicy_When_JsonIsValid()
    {
        ToolPermissionParser sut = new();

        ToolPermissionPolicy result = sut.Parse(
            "file_read",
            """
            {
              "approvalMode": "Automatic",
              "filePaths": [
                {
                  "argumentName": "path",
                  "kind": "Read",
                  "allowedRoots": [" src ", "src"]
                }
              ],
              "shell": {
                "commandArgumentName": " command ",
                "sandboxPermissionsArgumentName": " sandbox_permissions ",
                "justificationArgumentName": " justification ",
                "prefixRuleArgumentName": " prefix_rule ",
                "allowedCommands": [" git ", "git", "dotnet"]
              }
            }
            """);

        result.ApprovalMode.Should().Be(ToolApprovalMode.Automatic);
        result.FilePaths.Should().ContainSingle();
        result.FilePaths[0].AllowedRoots.Should().Equal("src");
        result.Shell.Should().NotBeNull();
        result.Shell!.CommandArgumentName.Should().Be("command");
        result.Shell.SandboxPermissionsArgumentName.Should().Be("sandbox_permissions");
        result.Shell.JustificationArgumentName.Should().Be("justification");
        result.Shell.PrefixRuleArgumentName.Should().Be("prefix_rule");
        result.Shell.AllowedCommands.Should().Equal("git", "dotnet");
    }

    [Fact]
    public void Parse_Should_NormalizeToolTagsAndAdditionalPermissionPolicies()
    {
        ToolPermissionParser sut = new();

        ToolPermissionPolicy result = sut.Parse(
            "apply_patch",
            """
            {
              "approvalMode": "Automatic",
              "toolTags": [" edit ", "EDIT"],
              "patch": {
                "patchArgumentName": " patch ",
                "kind": "Write",
                "allowedRoots": [" . ", "."]
              },
              "webRequest": {
                "requestArgumentName": " query "
              }
            }
            """);

        result.ToolTags.Should().Equal("edit");
        result.Patch.Should().NotBeNull();
        result.Patch!.PatchArgumentName.Should().Be("patch");
        result.Patch.AllowedRoots.Should().Equal(".");
        result.WebRequest.Should().NotBeNull();
        result.WebRequest!.RequestArgumentName.Should().Be("query");
        result.BypassUserPermissionRules.Should().BeFalse();
    }

    [Fact]
    public void Parse_Should_ReadBypassUserPermissionRules_When_Configured()
    {
        ToolPermissionParser sut = new();

        ToolPermissionPolicy result = sut.Parse(
            "planning_mode",
            """
            {
              "approvalMode": "Automatic",
              "bypassUserPermissionRules": true
            }
            """);

        result.BypassUserPermissionRules.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_Throw_When_ShellAllowlistIsEmpty()
    {
        ToolPermissionParser sut = new();

        Action action = () => sut.Parse(
            "shell_command",
            """
            {
              "approvalMode": "Automatic",
              "shell": {
                "commandArgumentName": "command",
                "allowedCommands": []
              }
            }
            """);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one allowed shell command*");
    }
}
