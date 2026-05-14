using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class ChatMessageTests
{
    [Fact]
    public void Should_Set_Properties()
    {
        var message = new ChatMessage
        {
            Id = 1,
            Role = Role.Assistant,
            Text = "Hello"
        };

        message.Id.Should().Be(1);
        message.Role.Should().Be(Role.Assistant);
        message.Text.Should().Be("Hello");
    }

    [Fact]
    public void Should_Default_Text_To_Empty()
    {
        var message = new ChatMessage();

        message.Text.Should().BeEmpty();
        message.Id.Should().Be(0);
        message.Role.Should().Be(Role.User);
    }

    [Fact]
    public void Should_Allow_Text_Update()
    {
        var message = new ChatMessage { Text = "Original" };
        message.Text = "Updated";

        message.Text.Should().Be("Updated");
    }
}
