using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Storage;
using System.Text.Json.Nodes;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class WorkspaceSettingsWriterTests : IDisposable
{
    private readonly string _workspaceRoot;

    public WorkspaceSettingsWriterTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-workspace-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task SavePermissionSettingsAsync_Should_WritePermissionsToWorkspaceProfile()
    {
        WorkspaceSettingsWriter sut = new();
        PermissionSettings settings = new()
        {
            AutoApproveAllTools = false,
            DefaultMode = PermissionMode.Deny,
            FileRead = PermissionMode.Allow,
            Network = PermissionMode.Ask,
            SandboxMode = ToolSandboxMode.ReadOnly
        };

        await sut.SavePermissionSettingsAsync(
            _workspaceRoot,
            settings,
            CancellationToken.None);

        JsonObject root = ReadProfile();
        JsonObject permissions = root["Application"]!["Permissions"]!.AsObject();
        permissions["auto_approve_all_tools"]!.GetValue<bool>().Should().BeFalse();
        permissions["defaultMode"]!.GetValue<string>().Should().Be("Deny");
        permissions["file_read"]!.GetValue<string>().Should().Be("Allow");
        permissions["network"]!.GetValue<string>().Should().Be("Ask");
        permissions["sandboxMode"]!.GetValue<string>().Should().Be("ReadOnly");
    }

    [Fact]
    public async Task SavePermissionSettingsAsync_Should_PreserveExistingSectionsAndPermissionFields()
    {
        WriteProfile(
            """
            {
              "memory": {
                "maxEntries": 250
              },
              "Application": {
                "Permissions": {
                  "shell_default": "Ask"
                }
              }
            }
            """);
        WorkspaceSettingsWriter sut = new();
        PermissionSettings settings = new()
        {
            DefaultMode = PermissionMode.Ask,
            SandboxMode = ToolSandboxMode.WorkspaceWrite
        };

        await sut.SavePermissionSettingsAsync(
            _workspaceRoot,
            settings,
            CancellationToken.None);

        JsonObject root = ReadProfile();
        root["memory"]!["maxEntries"]!.GetValue<int>().Should().Be(250);
        JsonObject permissions = root["Application"]!["Permissions"]!.AsObject();
        permissions["shell_default"]!.GetValue<string>().Should().Be("Ask");
        permissions["sandboxMode"]!.GetValue<string>().Should().Be("WorkspaceWrite");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private JsonObject ReadProfile()
    {
        string path = GetProfilePath();
        string json = File.ReadAllText(path);
        return JsonNode.Parse(json)!.AsObject();
    }

    private void WriteProfile(string json)
    {
        string path = GetProfilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private string GetProfilePath()
    {
        return Path.Combine(_workspaceRoot, ".nanoagent", "agent-profile.json");
    }
}
