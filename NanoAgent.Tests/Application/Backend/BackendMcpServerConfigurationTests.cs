using FluentAssertions;
using NanoAgent.Application.Backend;

namespace NanoAgent.Tests.Application.Backend;

public sealed class BackendMcpServerConfigurationTests
{
    [Fact]
    public void Should_Construct_With_Name()
    {
        var config = new BackendMcpServerConfiguration("my-server");

        config.Name.Should().Be("my-server");
    }

    [Fact]
    public void Should_Trim_Name()
    {
        var config = new BackendMcpServerConfiguration("  my-server  ");

        config.Name.Should().Be("my-server");
    }

    [Fact]
    public void Should_Throw_When_NameIsEmpty()
    {
        Action act = () => new BackendMcpServerConfiguration("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_NameIsNull()
    {
        Action act = () => new BackendMcpServerConfiguration(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Set_Default_Values()
    {
        var config = new BackendMcpServerConfiguration("test");

        config.Enabled.Should().BeTrue();
        config.StartupTimeoutSeconds.Should().Be(10);
        config.ToolTimeoutSeconds.Should().Be(60);
        config.Args.Should().BeEmpty();
        config.DisabledTools.Should().BeEmpty();
        config.EnabledTools.Should().BeEmpty();
        config.Env.Should().BeEmpty();
        config.EnvHttpHeaders.Should().BeEmpty();
        config.HttpHeaders.Should().BeEmpty();
        config.ToolApprovalModes.Should().BeEmpty();
        config.EnvVars.Should().BeEmpty();
    }

    [Fact]
    public void Mark_And_IsAssigned_Should_Track_PropertyAssignment()
    {
        var config = new BackendMcpServerConfiguration("test");

        config.IsAssigned("Command").Should().BeFalse();
        config.Mark("Command");
        config.IsAssigned("Command").Should().BeTrue();
    }

    [Fact]
    public void Should_Set_And_Get_Properties()
    {
        var config = new BackendMcpServerConfiguration("test")
        {
            Command = "node",
            Url = "http://localhost:8080",
            Cwd = "/app",
            BearerTokenEnvVar = "TOKEN",
            DefaultToolsApprovalMode = "auto",
            Enabled = false,
            Required = true,
            Source = "config",
            StartupTimeoutSeconds = 30,
            ToolTimeoutSeconds = 120
        };

        config.Command.Should().Be("node");
        config.Url.Should().Be("http://localhost:8080");
        config.Cwd.Should().Be("/app");
        config.BearerTokenEnvVar.Should().Be("TOKEN");
        config.DefaultToolsApprovalMode.Should().Be("auto");
        config.Enabled.Should().BeFalse();
        config.Required.Should().BeTrue();
        config.Source.Should().Be("config");
        config.StartupTimeoutSeconds.Should().Be(30);
        config.ToolTimeoutSeconds.Should().Be(120);
    }
}
