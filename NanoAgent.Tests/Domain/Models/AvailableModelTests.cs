using FluentAssertions;
using NanoAgent.Domain.Models;

namespace NanoAgent.Tests.Domain.Models;

public sealed class AvailableModelTests
{
    [Fact]
    public void Should_Store_Id_And_ContextWindowTokens()
    {
        var model = new AvailableModel("gpt-5-mini", 128_000);

        model.Id.Should().Be("gpt-5-mini");
        model.ContextWindowTokens.Should().Be(128_000);
    }

    [Fact]
    public void Should_Allow_Null_ContextWindowTokens()
    {
        var model = new AvailableModel("gpt-5-mini");

        model.Id.Should().Be("gpt-5-mini");
        model.ContextWindowTokens.Should().BeNull();
    }

    [Fact]
    public void EqualModels_Should_Be_Equal()
    {
        var model1 = new AvailableModel("gpt-5-mini", 128_000);
        var model2 = new AvailableModel("gpt-5-mini", 128_000);

        model1.Should().Be(model2);
    }

    [Fact]
    public void DifferentModels_Should_Not_Be_Equal()
    {
        var model1 = new AvailableModel("gpt-5-mini");
        var model2 = new AvailableModel("gpt-4.1");

        model1.Should().NotBe(model2);
    }
}
