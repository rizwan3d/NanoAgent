using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class RoleTests
{
    [Fact]
    public void Should_Have_Required_Values()
    {
        ((int)Role.User).Should().Be(0);
        ((int)Role.Assistant).Should().Be(1);
        ((int)Role.Thinking).Should().Be(2);
        ((int)Role.System).Should().Be(3);
    }

    [Fact]
    public void User_Should_Be_Default()
    {
        Role role = default;
        role.Should().Be(Role.User);
    }
}
