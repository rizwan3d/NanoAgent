using FluentAssertions;
using NanoAgent.CLI;

namespace NanoAgent.Tests.CLI;

public sealed class ReadPermissionChoiceTests
{
    [Fact]
    public void Should_Have_Required_Values()
    {
        ((int)ReadPermissionChoice.Allow).Should().Be(0);
        ((int)ReadPermissionChoice.Deny).Should().Be(1);
    }

    [Fact]
    public void Allow_Should_Be_Default()
    {
        ReadPermissionChoice choice = default;
        choice.Should().Be(ReadPermissionChoice.Allow);
    }
}
