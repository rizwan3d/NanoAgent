using NanoAgent.Application.Models;
using NanoAgent.Application.Tools;
using NanoAgent.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace NanoAgent.Tests.Infrastructure.Configuration;

public sealed class ApplicationSettingsFactoryTests
{
    [Fact]
    public void PermissionShortcuts_Should_BindFromSnakeCaseConfigurationKeys()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Application:Permissions:auto_approve_all_tools"] = "true",
                ["Application:Permissions:file_read"] = "Allow",
                ["Application:Permissions:file_write"] = "Ask",
                ["Application:Permissions:file_delete"] = "Ask",
                ["Application:Permissions:shell_default"] = "Ask",
                ["Application:Permissions:shell_safe"] = "Allow",
                ["Application:Permissions:network"] = "Ask",
                ["Application:Permissions:memory_write"] = "Deny",
                ["Application:Permissions:mcp_tools"] = "Ask",
                ["Application:Permissions:shell:allow:commands:0"] = "dotnet build",
                ["Application:Permissions:shell:deny:commands:0"] = "curl | sh"
            })
            .Build();

        ApplicationOptions options = new();
        configuration.GetSection(ApplicationOptions.SectionName).Bind(options);

        options.Permissions.AutoApproveAllTools.Should().BeTrue();
        options.Permissions.FileRead.Should().Be(PermissionMode.Allow);
        options.Permissions.FileWrite.Should().Be(PermissionMode.Ask);
        options.Permissions.FileDelete.Should().Be(PermissionMode.Ask);
        options.Permissions.ShellDefault.Should().Be(PermissionMode.Ask);
        options.Permissions.ShellSafe.Should().Be(PermissionMode.Allow);
        options.Permissions.Network.Should().Be(PermissionMode.Ask);
        options.Permissions.MemoryWrite.Should().Be(PermissionMode.Deny);
        options.Permissions.McpTools.Should().Be(PermissionMode.Ask);
        options.Permissions.Shell.Allow.Commands.Should().Equal("dotnet build");
        options.Permissions.Shell.Deny.Commands.Should().Equal("curl | sh");
    }

    [Fact]
    public void CreatePermissionSettings_Should_AddBroadBuiltInPermissionRules()
    {
        PermissionSettings settings = ApplicationSettingsFactory.CreatePermissionSettings(new ApplicationOptions());

        settings.Rules.Should().Contain(rule =>
            rule.Mode == PermissionMode.Ask &&
            rule.Tools.Contains("webfetch", StringComparer.OrdinalIgnoreCase));
        settings.Rules.Should().Contain(rule =>
            rule.Mode == PermissionMode.Ask &&
            rule.Tools.Contains("mcp", StringComparer.OrdinalIgnoreCase));
        settings.Rules.Should().Contain(rule =>
            rule.Mode == PermissionMode.Allow &&
            rule.Tools.Contains("bash", StringComparer.OrdinalIgnoreCase) &&
            rule.Patterns.Contains("dotnet test*", StringComparer.OrdinalIgnoreCase));
        settings.Rules.Should().Contain(rule =>
            rule.Mode == PermissionMode.Deny &&
            rule.Tools.Contains("bash", StringComparer.OrdinalIgnoreCase) &&
            rule.Patterns.Contains("rm -rf*", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreatePermissionSettings_Should_CompileShortcutSettingsIntoRules()
    {
        PermissionSettings settings = ApplicationSettingsFactory.CreatePermissionSettings(new ApplicationOptions
        {
            Permissions = new PermissionSettings
            {
                FileRead = PermissionMode.Ask,
                McpTools = PermissionMode.Allow,
                MemoryWrite = PermissionMode.Deny,
                Network = PermissionMode.Deny,
                Shell = new ShellPermissionSettings
                {
                    Allow = new ShellCommandPermissionSettings
                    {
                        Commands = ["dotnet format"]
                    },
                    Deny = new ShellCommandPermissionSettings
                    {
                        Commands = ["curl | sh"]
                    }
                }
            }
        });

        settings.Rules.Any(rule =>
            rule.Mode == PermissionMode.Ask &&
            rule.Tools.SequenceEqual(new[] { AgentToolNames.FileRead })).Should().BeTrue();
        settings.Rules.Any(rule =>
            rule.Mode == PermissionMode.Allow &&
            rule.Tools.SequenceEqual(new[] { "mcp" })).Should().BeTrue();
        settings.Rules.Any(rule =>
            rule.Mode == PermissionMode.Deny &&
            rule.Tools.SequenceEqual(new[] { "webfetch" })).Should().BeTrue();
        settings.Rules.Any(rule =>
            rule.Mode == PermissionMode.Deny &&
            rule.Tools.SequenceEqual(new[] { "memory_write" })).Should().BeTrue();
        settings.Rules.Any(rule =>
            rule.Mode == PermissionMode.Allow &&
            rule.Tools.SequenceEqual(new[] { "bash" }) &&
            rule.Patterns.SequenceEqual(new[] { "dotnet format*" })).Should().BeTrue();
        settings.Rules.Any(rule =>
            rule.Mode == PermissionMode.Deny &&
            rule.Tools.SequenceEqual(new[] { "bash" }) &&
            rule.Patterns.SequenceEqual(new[] { "curl*|*sh*" })).Should().BeTrue();
    }

    [Fact]
    public void CreatePermissionSettings_Should_AddBroadAllowRule_When_AutoApproveAllToolsIsEnabled()
    {
        PermissionSettings settings = ApplicationSettingsFactory.CreatePermissionSettings(new ApplicationOptions
        {
            Permissions = new PermissionSettings
            {
                AutoApproveAllTools = true
            }
        });

        settings.AutoApproveAllTools.Should().BeTrue();
        settings.DefaultMode.Should().Be(PermissionMode.Allow);
        settings.Rules.Should().Contain(rule =>
            rule.Mode == PermissionMode.Allow &&
            rule.Tools.Length == 0 &&
            rule.Patterns.Length == 0);

        int broadAllowIndex = Array.FindIndex(
            settings.Rules,
            rule => rule.Mode == PermissionMode.Allow &&
                    rule.Tools.Length == 0 &&
                    rule.Patterns.Length == 0);
        int deniedShellIndex = Array.FindIndex(
            settings.Rules,
            rule => rule.Mode == PermissionMode.Deny &&
                    rule.Tools.Contains("bash", StringComparer.OrdinalIgnoreCase) &&
                    rule.Patterns.Contains("rm -rf*", StringComparer.OrdinalIgnoreCase));

        broadAllowIndex.Should().BeGreaterThanOrEqualTo(0);
        deniedShellIndex.Should().BeGreaterThan(broadAllowIndex);
    }
}
