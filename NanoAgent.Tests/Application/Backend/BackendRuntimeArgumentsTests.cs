using FluentAssertions;
using NanoAgent.Application.Backend;

namespace NanoAgent.Tests.Application.Backend;

public sealed class BackendRuntimeArgumentsTests
{
    [Fact]
    public void Parse_Should_ExtractSessionOptions_AndPreserveRawArgs()
    {
        BackendRuntimeArguments arguments = BackendRuntimeArguments.Parse(
            [
                "--profile", "review",
                "--section=section-1",
                "--thinking", "off",
                "--surface", "VSCode",
                "--no-update-check"
            ]);

        arguments.RawArgs.Should().Equal(
            "--profile", "review",
            "--section=section-1",
            "--thinking", "off",
            "--surface", "VSCode",
            "--no-update-check");
        arguments.SectionId.Should().Be("section-1");
        arguments.ProfileName.Should().Be("review");
        arguments.ThinkingMode.Should().Be("off");
        arguments.AppSurface.Should().Be(BackendRuntimeOptions.VsCodeSurface);
        arguments.SkipUpdateCheck.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_UseLastSessionValue_ButPreserveFirstSurfaceBehavior()
    {
        BackendRuntimeArguments arguments = BackendRuntimeArguments.Parse(
            [
                "--session", "section-1",
                "--section", "section-2",
                "--surface", "desktop",
                "--surface", "jetbrains"
            ]);

        arguments.SectionId.Should().Be("section-2");
        arguments.AppSurface.Should().Be(BackendRuntimeOptions.DesktopSurface);
    }

    [Fact]
    public void WithDefaults_Should_ApplyFallbackSurface_AndSkipUpdateCheck()
    {
        BackendRuntimeArguments arguments = BackendRuntimeArguments.Empty.WithDefaults(
            BackendRuntimeOptions.DesktopSurface,
            skipUpdateCheck: true);

        arguments.RawArgs.Should().BeEmpty();
        arguments.AppSurface.Should().Be(BackendRuntimeOptions.DesktopSurface);
        arguments.SkipUpdateCheck.Should().BeTrue();
    }

    [Fact]
    public void Parse_Should_ThrowForMissingRecognizedOptionValue()
    {
        Action act = () => BackendRuntimeArguments.Parse(["--thinking"]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Missing value for --thinking.");
    }
}
