using NanoAgent.ConsoleHost.Rendering;
using NanoAgent.Tests.ConsoleHost.TestDoubles;
using FluentAssertions;
using Spectre.Console;

namespace NanoAgent.Tests.ConsoleHost.Rendering;

public sealed class SpectreConsoleFactoryTests
{
    [Fact]
    public void Create_Should_TreatBareCarriageReturn_AsLineRewrite()
    {
        FakeConsoleTerminal terminal = new();
        IAnsiConsole console = SpectreConsoleFactory.Create(terminal);

        console.Write(new Text("Edited"));
        console.Write(new Text("\rWorking"));
        console.WriteLine();

        terminal.Output.Should().Be($"Working{Environment.NewLine}");
    }
}
