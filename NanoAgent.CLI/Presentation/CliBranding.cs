using Spectre.Console;

namespace NanoAgent.CLI;

internal static class CliBranding
{
    private const string ApplicationName = "NanoAgent";
    private const string RepositoryUrl = "github.com/rizwan3d/NanoAgent";
    private const string SponsorName = "ALFAIN Technologies (PVT) Limited";
    private const string SponsorUrl = "https://alfain.co/";

    private static readonly (string Nano, string Agent)[] Wordmark =
    [
        (
            "███╗   ██╗  █████╗  ███╗   ██╗  ██████╗",
            "  █████╗   ██████╗   ███████╗  ███╗   ██╗  ████████╗"
        ),
        (
            "████╗  ██║ ██╔══██╗ ████╗  ██║ ██╔═══██╗",
            " ██╔══██╗ ██╔════╝  ██╔════╝  ████╗  ██║  ╚══██╔══╝"
        ),
        (
            "██╔██╗ ██║ ███████║ ██╔██╗ ██║ ██║   ██║",
            " ███████║ ██║  ███╗ █████╗    ██╔██╗ ██║     ██║"
        ),
        (
            "██║╚██╗██║ ██╔══██║ ██║╚██╗██║ ██║   ██║",
            " ██╔══██║ ██║   ██║ ██╔══╝    ██║╚██╗██║     ██║"
        ),
        (
            "██║ ╚████║ ██║  ██║ ██║ ╚████║ ╚██████╔╝",
            " ██║  ██║ ╚██████╔╝ ███████╗  ██║ ╚████║     ██║"
        ),
        (
            "╚═╝  ╚═══╝ ╚═╝  ╚═╝ ╚═╝  ╚═══╝  ╚═════╝",
            "  ╚═╝  ╚═╝  ╚═════╝  ╚══════╝  ╚═╝  ╚═══╝     ╚═╝"
        )
    ];

    internal static string BuildStatusHeaderMarkup()
    {
        return $"[bold cyan]{ApplicationName}[/]" +
            $" ── [grey]GitHub:[/] [deepskyblue1]{Markup.Escape(RepositoryUrl)} [/]";
    }

    internal static string BuildHeaderBodyMarkup()
    {
        List<string> lines = [];

        for (int index = 0; index < Wordmark.Length; index++)
        {
            string accentColor = index < 3 ? "fuchsia" : "purple";
            lines.Add(
                $"[grey]  [/][{accentColor}]   [/][white]{Markup.Escape(Wordmark[index].Nano)}[/][fuchsia]{Markup.Escape(Wordmark[index].Agent)}[/]");
        }

        lines.Add(
            $"[grey]  Sponsor:[/] [yellow]{Markup.Escape(SponsorName)}[/] [grey]([/][italic]{Markup.Escape(SponsorUrl)}[/][grey])[/]");
        lines.Add("[grey]  [/]");

        return string.Join('\n', lines);
    }
}
