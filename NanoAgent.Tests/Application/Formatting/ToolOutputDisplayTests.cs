using FluentAssertions;
using NanoAgent.Application.Formatting;

namespace NanoAgent.Tests.Application.Formatting;

public sealed class ToolOutputDisplayTests
{
    [Theory]
    [InlineData("full", true)]
    [InlineData("complete", true)]
    [InlineData("all", true)]
    [InlineData("on", true)]
    [InlineData("compact", false)]
    [InlineData("preview", false)]
    [InlineData("off", false)]
    public void ParsePreference_Should_MapKnownValues(string value, bool expected)
    {
        ToolOutputDisplay.ParsePreference(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    public void ParsePreference_Should_ReturnNull_ForEmptyOrUnknownValues(string? value)
    {
        ToolOutputDisplay.ParsePreference(value).Should().BeNull();
    }

    [Fact]
    public void ShowFullToolOutput_Should_FallBackToConfiguredDefault_WhenNoOverrideOrProfile()
    {
        WithState(() =>
        {
            ToolOutputDisplay.FullToolOutputOverride = null;
            ToolOutputDisplay.ProfileFullToolOutput = null;
            ToolOutputDisplay.ConfiguredDefaultFullToolOutput = true;

            ToolOutputDisplay.ShowFullToolOutput.Should().BeTrue();
        });
    }

    [Fact]
    public void ShowFullToolOutput_Should_LetProfilePreferenceWinOverConfiguredDefault()
    {
        WithState(() =>
        {
            ToolOutputDisplay.FullToolOutputOverride = null;
            ToolOutputDisplay.ProfileFullToolOutput = false;
            ToolOutputDisplay.ConfiguredDefaultFullToolOutput = true;

            ToolOutputDisplay.ShowFullToolOutput.Should().BeFalse();
        });
    }

    [Fact]
    public void ShowFullToolOutput_Should_LetCommandOverrideWinOverEverything()
    {
        WithState(() =>
        {
            ToolOutputDisplay.FullToolOutputOverride = false;
            ToolOutputDisplay.ProfileFullToolOutput = true;
            ToolOutputDisplay.ConfiguredDefaultFullToolOutput = true;

            ToolOutputDisplay.ShowFullToolOutput.Should().BeFalse();
        });
    }

    private static void WithState(Action action)
    {
        bool? previousOverride = ToolOutputDisplay.FullToolOutputOverride;
        bool? previousProfile = ToolOutputDisplay.ProfileFullToolOutput;
        bool? previousConfigured = ToolOutputDisplay.ConfiguredDefaultFullToolOutput;
        try
        {
            action();
        }
        finally
        {
            ToolOutputDisplay.FullToolOutputOverride = previousOverride;
            ToolOutputDisplay.ProfileFullToolOutput = previousProfile;
            ToolOutputDisplay.ConfiguredDefaultFullToolOutput = previousConfigured;
        }
    }
}
