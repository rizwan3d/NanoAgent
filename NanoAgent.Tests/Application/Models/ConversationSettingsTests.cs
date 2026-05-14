using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ConversationSettingsTests
{
    [Fact]
    public void Should_Store_All_Properties()
    {
        var settings = new ConversationSettings(
            "You are a helpful assistant.",
            TimeSpan.FromSeconds(60),
            50,
            25);

        settings.SystemPrompt.Should().Be("You are a helpful assistant.");
        settings.RequestTimeout.Should().Be(TimeSpan.FromSeconds(60));
        settings.MaxHistoryTurns.Should().Be(50);
        settings.MaxToolRoundsPerTurn.Should().Be(25);
    }

    [Fact]
    public void Should_Allow_Null_SystemPrompt()
    {
        var settings = new ConversationSettings(null, TimeSpan.FromSeconds(30), 10, 5);

        settings.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public void EqualSettings_Should_Be_Equal()
    {
        var s1 = new ConversationSettings("prompt", TimeSpan.FromSeconds(30), 10, 5);
        var s2 = new ConversationSettings("prompt", TimeSpan.FromSeconds(30), 10, 5);

        s1.Should().Be(s2);
    }
}
